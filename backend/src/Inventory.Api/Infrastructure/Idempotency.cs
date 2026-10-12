using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Inventory.Application.Common;
using Inventory.Domain.Common;
using Inventory.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Inventory.Api.Infrastructure;

/// <summary>
/// Marks a stock-changing command as idempotent: a request with an <c>Idempotency-Key</c> header runs once per
/// user and key; retries with the same key and body receive the stored response (header <c>Idempotent-Replayed: true</c>).
/// The same key with a different request returns 422; a retry while the first request is still running returns 409.
/// Requests without the header run normally (natural idempotency still applies, e.g. "already posted").
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class IdempotentAttribute : Attribute, IFilterFactory
{
    public bool IsReusable => true;

    public IFilterMetadata CreateInstance(IServiceProvider serviceProvider) => ActivatorUtilities.CreateInstance<IdempotencyFilter>(serviceProvider);
}

internal sealed class IdempotencyFilter(
    IServiceScopeFactory scopes, ICurrentUser user, TimeProvider clock, IOptions<JsonOptions> json, ILogger<IdempotencyFilter> logger) : IAsyncActionFilter
{
    public const string Header = "Idempotency-Key";
    public const string ReplayedHeader = "Idempotent-Replayed";
    public const int MaxKeyLength = 100;

    /// <summary>An in-progress record older than this is assumed abandoned (crash) and the request may run again.</summary>
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(5);

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var http = context.HttpContext;
        var key = http.Request.Headers[Header].ToString().Trim();
        if (key.Length == 0 || user.IdOrNull is not { } userId)
        {
            await next();
            return;
        }
        if (key.Length > MaxKeyLength)
        {
            throw new DomainRuleException("idempotency.key_invalid", $"The {Header} header must be at most {MaxKeyLength} characters.");
        }

        var endpoint = $"{http.Request.Method} {http.Request.Path}";
        var hash = Hash(endpoint, context.ActionArguments);
        var ct = http.RequestAborted;

        // The record lives in its own scope/connection so it is written independently of the command's transaction.
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var record = new IdempotencyRecord { UserId = userId, Key = key, Endpoint = endpoint, RequestHash = hash, State = IdempotencyState.InProgress, CreatedAt = clock.GetUtcNow() };
        if (!await TryInsertAsync(db, record, ct))
        {
            var existing = await db.IdempotencyRecords.AsNoTracking().SingleAsync(r => r.UserId == userId && r.Key == key, ct);
            if (existing.RequestHash != hash)
            {
                throw new DomainRuleException("idempotency.key_reused", "This idempotency key was already used for a different request.");
            }
            if (existing.State == IdempotencyState.Completed)
            {
                http.Response.Headers[ReplayedHeader] = "true";
                context.Result = new ContentResult
                {
                    StatusCode = existing.ResponseStatus,
                    Content = existing.ResponseBody,
                    ContentType = existing.ResponseContentType,
                };
                return;
            }
            if (clock.GetUtcNow() - existing.CreatedAt < StaleAfter)
            {
                throw new DomainRuleException("idempotency.in_progress", "This request is already being processed. Wait a moment and reload.", isConflict: true);
            }
            // Abandoned attempt: take it over and run again (the command's own status checks prevent a double effect).
            record = await db.IdempotencyRecords.AsTracking().SingleAsync(r => r.Id == existing.Id, ct);
            record.CreatedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(ct);
        }

        var executed = await next();
        try
        {
            if (executed.Exception is null && Capture(executed.Result, out var status, out var body, out var contentType) && status is >= 200 and < 300)
            {
                record.State = IdempotencyState.Completed;
                record.ResponseStatus = status;
                record.ResponseBody = body;
                record.ResponseContentType = contentType;
                record.CompletedAt = clock.GetUtcNow();
                await db.SaveChangesAsync(CancellationToken.None);
            }
            else
            {
                // Failed: nothing was committed, so the client may retry with the same key.
                db.IdempotencyRecords.Remove(record);
                await db.SaveChangesAsync(CancellationToken.None);
            }
        }
        catch (Exception e) when (e is DbUpdateException or NpgsqlException)
        {
            logger.LogWarning(e, "Could not store the idempotency record for {Endpoint}", endpoint);
        }
    }

    /// <summary>Inserts the record unless the user already used the key (ON CONFLICT avoids a logged unique-violation error).</summary>
    private static async Task<bool> TryInsertAsync(AppDbContext db, IdempotencyRecord record, CancellationToken ct)
    {
        var inserted = await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO idempotency_records (id, user_id, key, endpoint, request_hash, state, created_at)
             VALUES ({record.Id}, {record.UserId}, {record.Key}, {record.Endpoint}, {record.RequestHash}, {record.State.ToString()}, {record.CreatedAt})
             ON CONFLICT (user_id, key) DO NOTHING
             """, ct);
        if (inserted == 0) return false;
        db.IdempotencyRecords.Attach(record);
        return true;
    }

    private bool Capture(IActionResult? result, out int status, out string? body, out string? contentType)
    {
        switch (result)
        {
            case ObjectResult o:
                status = o.StatusCode ?? StatusCodes.Status200OK;
                body = JsonSerializer.Serialize(o.Value, o.DeclaredType ?? o.Value?.GetType() ?? typeof(object), json.Value.JsonSerializerOptions);
                contentType = "application/json; charset=utf-8";
                return true;
            case StatusCodeResult s:
                status = s.StatusCode;
                body = null;
                contentType = null;
                return true;
            default:
                status = 0;
                body = null;
                contentType = null;
                return false;
        }
    }

    private string Hash(string endpoint, IDictionary<string, object?> arguments)
    {
        var payload = arguments.Where(a => a.Value is not CancellationToken).OrderBy(a => a.Key, StringComparer.Ordinal)
            .ToDictionary(a => a.Key, a => a.Value);
        var text = endpoint + "\n" + JsonSerializer.Serialize(payload, json.Value.JsonSerializerOptions);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }
}
