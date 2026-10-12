using System.Linq.Expressions;
using Inventory.Application.Common;
using Inventory.Domain.Auditing;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Application.Reporting;

public sealed record AuditEntryDto(long Id, DateTimeOffset OccurredAt, string Action, string? EntityType, string? EntityId, Guid? WarehouseId, string? WarehouseCode,
    Guid? UserId, string? UserName, string? Data, string? IpAddress);

public sealed class AuditListQuery : ListQuery
{
    /// <summary>Exact action (e.g. <c>dispatch.shipped</c>) or a prefix ending with a dot (<c>dispatch.</c>).</summary>
    public string? Action { get; set; }
    public string? EntityType { get; set; }
    public string? EntityId { get; set; }
    public Guid? WarehouseId { get; set; }
    public Guid? UserId { get; set; }
    public DateTimeOffset? From { get; set; }
    public DateTimeOffset? To { get; set; }
}

/// <summary>
/// The audit log (permission <c>audit.read</c>). Entries tied to a warehouse are only shown for warehouses the user can
/// access; entries without a warehouse (users, products, settings) are visible to every auditor.
/// </summary>
public sealed class AuditQueries(IApplicationDbContext db, IWarehouseAccess access, IUserDirectory users)
{
    private static readonly Dictionary<string, Expression<Func<AuditLog, object?>>> Sorts = new()
    {
        ["occurredAt"] = a => a.OccurredAt,
        ["action"] = a => a.Action,
    };

    public async Task<PagedResult<AuditEntryDto>> ListAsync(AuditListQuery query, CancellationToken cancellationToken)
    {
        var all = await access.HasAllWarehousesAsync(cancellationToken);
        var accessible = await access.GetAccessibleWarehouseIdsAsync(cancellationToken);
        var q = db.AuditLogs.AsNoTracking().AsQueryable();
        if (!all) q = q.Where(a => a.WarehouseId == null || accessible.Contains(a.WarehouseId.Value));

        if (!string.IsNullOrWhiteSpace(query.Action))
        {
            var action = query.Action.Trim();
            q = action.EndsWith('.') ? q.Where(a => a.Action.StartsWith(action)) : q.Where(a => a.Action == action);
        }
        if (!string.IsNullOrWhiteSpace(query.EntityType)) q = q.Where(a => a.EntityType == query.EntityType);
        if (!string.IsNullOrWhiteSpace(query.EntityId)) q = q.Where(a => a.EntityId == query.EntityId);
        if (query.WarehouseId is { } wh) q = q.Where(a => a.WarehouseId == wh);
        if (query.UserId is { } uid) q = q.Where(a => a.UserId == uid);
        if (query.From is { } from) q = q.Where(a => a.OccurredAt >= from);
        if (query.To is { } to) q = q.Where(a => a.OccurredAt < to);
        if (query.Term is { } term) q = q.Where(a => a.Action.Contains(term) || (a.Data != null && a.Data.ToLower().Contains(term)) || (a.EntityId != null && a.EntityId.Contains(term)));
        if (query.SortBy is null) { query.SortBy = "occurredAt"; query.SortDir ??= "desc"; }

        var page = await q.SortBy(query, Sorts, "occurredAt", a => a.Id).ToPagedAsync(query, cancellationToken);
        var names = await users.NamesAsync(page.Items.Select(a => a.UserId), cancellationToken);
        var whIds = page.Items.Where(a => a.WarehouseId != null).Select(a => a.WarehouseId!.Value).Distinct().ToList();
        var codes = await db.Warehouses.AsNoTracking().Where(w => whIds.Contains(w.Id)).ToDictionaryAsync(w => w.Id, w => w.Code, cancellationToken);
        return new PagedResult<AuditEntryDto>(page.Items.Select(a => new AuditEntryDto(a.Id, a.OccurredAt, a.Action, a.EntityType, a.EntityId, a.WarehouseId,
            a.WarehouseId is { } w ? codes.GetValueOrDefault(w) : null, a.UserId, names.NameOf(a.UserId), a.Data, a.IpAddress)).ToList(), page.Page, page.PageSize, page.TotalCount);
    }
}
