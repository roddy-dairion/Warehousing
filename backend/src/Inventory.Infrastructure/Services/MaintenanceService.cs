using Inventory.Application.Inventory;
using Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Inventory.Infrastructure.Services;

/// <summary>
/// Hourly housekeeping: deletes expired idempotency records and, every <see cref="InventoryOptions.ReconciliationIntervalHours"/>,
/// checks the stock invariants. Reconciliation differences are logged as errors; data is never changed automatically.
/// </summary>
internal sealed partial class MaintenanceService(IServiceScopeFactory scopes, IOptions<InventoryOptions> options, TimeProvider clock, ILogger<MaintenanceService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        DateTimeOffset? lastReconciliation = null;
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1), clock);
        do
        {
            try
            {
                await CleanUpIdempotencyAsync(stoppingToken);
                var now = clock.GetUtcNow();
                if (lastReconciliation is null || now - lastReconciliation >= TimeSpan.FromHours(Math.Max(1, options.Value.ReconciliationIntervalHours)))
                {
                    await ReconcileAsync(stoppingToken);
                    lastReconciliation = now;
                }
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                LogFailed(logger, e);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task CleanUpIdempotencyAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cutoff = clock.GetUtcNow().AddHours(-Math.Max(1, options.Value.IdempotencyRetentionHours));
        var deleted = await db.IdempotencyRecords.Where(r => r.CreatedAt < cutoff).ExecuteDeleteAsync(ct);
        if (deleted > 0) LogCleaned(logger, deleted);
    }

    private async Task ReconcileAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<ReconciliationService>().CheckAsync(null, ct);
        if (result.Issues.Count == 0)
        {
            LogReconciled(logger, result.BalancesChecked);
            return;
        }
        foreach (var i in result.Issues)
        {
            LogMismatch(logger, i.Sku, i.LocationCode, i.LotNo, i.OnHand, i.LedgerOnHand, i.Reserved, i.OpenReservations, i.BalanceId);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Maintenance run failed")]
    private static partial void LogFailed(ILogger logger, Exception e);

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleted {Count} expired idempotency records")]
    private static partial void LogCleaned(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Stock reconciliation OK: {Count} balances checked")]
    private static partial void LogReconciled(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Stock reconciliation mismatch for {Sku} in {Bin} lot {Lot}: on hand {OnHand} vs ledger {Ledger}, reserved {Reserved} vs open reservations {Open} (balance {BalanceId})")]
    private static partial void LogMismatch(ILogger logger, string sku, string bin, string? lot, decimal onHand, decimal ledger, decimal reserved, decimal open, Guid balanceId);
}
