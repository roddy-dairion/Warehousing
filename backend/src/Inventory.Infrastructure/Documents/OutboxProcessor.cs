using Inventory.Application.Common;
using Inventory.Application.Documents;
using Inventory.Domain.Documents;
using Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Inventory.Infrastructure.Documents;

/// <summary>Retries pending documents and sends due emails. Each item runs in its own scope.</summary>
public sealed partial class OutboxProcessor(IServiceScopeFactory scopes, IOptions<EmailOptions> emailOptions, TimeProvider clock, ILogger<OutboxProcessor> logger)
{
    public async Task<int> ProcessDocumentsAsync(CancellationToken cancellationToken)
    {
        List<Guid> ids;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            ids = await db.DispatchDocuments.AsNoTracking().Where(d => d.Status == DocumentStatus.Pending).OrderBy(d => d.RequestedAt)
                .Select(d => d.Id).Take(20).ToListAsync(cancellationToken);
        }
        var done = 0;
        foreach (var id in ids)
        {
            await using var scope = scopes.CreateAsyncScope();
            if (await scope.ServiceProvider.GetRequiredService<DocumentService>().TryGenerateAsync(id, cancellationToken)) done++;
        }
        return done;
    }

    /// <summary>Sends emails that are due. A row lock (SKIP LOCKED) stops two instances sending the same email.</summary>
    public async Task<int> ProcessEmailsAsync(CancellationToken cancellationToken)
    {
        if (emailOptions.Value.Mode == EmailMode.Disabled) return 0;
        var sent = 0;
        for (var i = 0; i < 50; i++)
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
            var now = clock.GetUtcNow();
            var message = await db.EmailMessages
                .FromSqlInterpolated($"SELECT *, xmin FROM email_messages WHERE status = 'Pending' AND next_attempt_at <= {now} ORDER BY next_attempt_at LIMIT 1 FOR UPDATE SKIP LOCKED")
                .AsTracking().ToListAsync(cancellationToken).ContinueWith(t => t.Result.FirstOrDefault(), cancellationToken, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
            if (message is null) break;

            try
            {
                var attachments = new List<EmailAttachment>();
                var files = scope.ServiceProvider.GetRequiredService<IFileStorage>();
                foreach (var fileId in message.AttachmentFileIds)
                {
                    var f = await db.StoredFiles.AsNoTracking().SingleAsync(x => x.Id == fileId, cancellationToken);
                    await using var stream = await files.OpenReadAsync(f.StorageKey, cancellationToken) ?? throw new InvalidOperationException($"Attachment {f.FileName} is missing.");
                    using var ms = new MemoryStream();
                    await stream.CopyToAsync(ms, cancellationToken);
                    attachments.Add(new EmailAttachment(f.FileName, f.ContentType, ms.ToArray()));
                }
                await scope.ServiceProvider.GetRequiredService<IEmailSender>().SendAsync(new OutgoingEmail(
                    Split(message.To), Split(message.Bcc), message.Subject, message.Body, attachments), cancellationToken);
                message.MarkSent(clock.GetUtcNow());
                sent++;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                message.MarkFailed(e.Message, clock.GetUtcNow());
                LogSendFailed(logger, e, message.Id, message.Attempts);
            }
            await db.SaveChangesAsync(CancellationToken.None);
            await tx.CommitAsync(CancellationToken.None);
        }
        return sent;
    }

    private static List<string> Split(string? addresses) =>
        (addresses ?? string.Empty).Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Email {EmailId} could not be sent (attempt {Attempt})")]
    private static partial void LogSendFailed(ILogger logger, Exception e, Guid emailId, int attempt);
}

internal sealed partial class OutboxWorker(OutboxProcessor processor, IOptions<EmailOptions> options, TimeProvider clock, ILogger<OutboxWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, options.Value.PollSeconds)), clock);
        do
        {
            try
            {
                await processor.ProcessDocumentsAsync(stoppingToken);
                await processor.ProcessEmailsAsync(stoppingToken);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                LogFailed(logger, e);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox run failed")]
    private static partial void LogFailed(ILogger logger, Exception e);
}
