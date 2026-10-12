using System.Globalization;
using System.Net.Mail;
using System.Text;
using Inventory.Application.Common;
using Inventory.Domain.Common;
using Inventory.Domain.Documents;
using Inventory.Domain.Purchasing;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Application.Purchasing;

/// <summary>
/// One product below its minimum stock in a warehouse. All quantities are in the base unit, except
/// <see cref="SuggestedPurchaseQuantity"/> (in <see cref="PurchaseUnitCode"/>).
/// </summary>
public sealed record ReorderSuggestionDto(
    Guid ProductId, string Sku, string ProductName, string BaseUnitCode,
    decimal MinStockQty, decimal? MaxStockQty, decimal OnHand, decimal Reserved, decimal Available, decimal OnOrder,
    /// <summary>Order-up-to level minus available and on order, rounded up to whole purchase units; 0 = already covered by open orders.</summary>
    decimal SuggestedQuantity,
    Guid? PurchaseProductUnitId, string PurchaseUnitCode, decimal PurchaseFactor, decimal SuggestedPurchaseQuantity,
    Guid? SupplierId, string? SupplierCode, string? SupplierName, int? SupplierLeadTimeDays,
    IReadOnlyList<string> OpenOrderNumbers);

public sealed record PurchasingSettingsDto(string? LowStockAlertEmails);

public sealed record UpdatePurchasingSettingsRequest(string? LowStockAlertEmails);

/// <summary>
/// Reorder suggestions: products whose available stock (on hand − reserved) is below their minimum. The suggested
/// quantity brings available + on order up to the order-up-to level (or the minimum when none is set).
/// </summary>
public sealed class ReorderService(IApplicationDbContext db, IWarehouseAccess access, TimeProvider clock, IAuditWriter audit)
{
    /// <summary>Local hour (company time zone) from which the daily low-stock email is sent.</summary>
    public const int AlertHour = 7;

    public async Task<IReadOnlyList<ReorderSuggestionDto>> SuggestionsAsync(Guid warehouseId, CancellationToken cancellationToken)
    {
        await access.EnsureCanAccessAsync(warehouseId, cancellationToken);
        return await ComputeAsync(warehouseId, cancellationToken);
    }

    /// <summary>Number of products that need ordering (below minimum and not covered by open orders).</summary>
    public async Task<int> CountToOrderAsync(Guid warehouseId, CancellationToken cancellationToken) =>
        (await ComputeAsync(warehouseId, cancellationToken)).Count(s => s.SuggestedQuantity > 0);

    /// <summary>Without an access check: callers check the warehouse (or run as the system).</summary>
    internal async Task<List<ReorderSuggestionDto>> ComputeAsync(Guid warehouseId, CancellationToken cancellationToken)
    {
        var rows = await (
            from p in db.Products.AsNoTracking()
            where p.IsActive && p.MinStockQty != null
            let onHand = db.InventoryBalances.Where(b => b.WarehouseId == warehouseId && b.ProductId == p.Id).Sum(b => (decimal?)b.OnHand) ?? 0
            let reserved = db.InventoryBalances.Where(b => b.WarehouseId == warehouseId && b.ProductId == p.Id).Sum(b => (decimal?)b.Reserved) ?? 0
            where onHand - reserved < p.MinStockQty
            select new
            {
                p.Id, p.Sku, p.Name, p.MinStockQty, p.MaxStockQty, p.DefaultSupplierId, p.PurchaseUnitId, p.BaseUnitId,
                OnHand = onHand, Reserved = reserved,
                Units = p.Units.Select(u => new { u.Id, u.UnitId, u.FactorToBase }).ToList(),
            }).ToListAsync(cancellationToken);
        if (rows.Count == 0) return [];

        var productIds = rows.Select(r => r.Id).ToList();
        var open = await db.PurchaseOrders.AsNoTracking()
            .Where(o => o.WarehouseId == warehouseId && (o.Status == PurchaseOrderStatus.Ordered || o.Status == PurchaseOrderStatus.PartiallyReceived))
            .SelectMany(o => o.Lines.Where(l => productIds.Contains(l.ProductId) && l.Quantity > l.ReceivedQuantity)
                .Select(l => new { l.ProductId, o.Number, Outstanding = l.Quantity - l.ReceivedQuantity }))
            .ToListAsync(cancellationToken);
        var supplierIds = rows.Where(r => r.DefaultSupplierId != null).Select(r => r.DefaultSupplierId!.Value).Distinct().ToList();
        var suppliers = await db.Suppliers.AsNoTracking().Where(s => supplierIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, cancellationToken);
        var units = await db.UnitsOfMeasure.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.Code, cancellationToken);

        var result = new List<ReorderSuggestionDto>();
        foreach (var r in rows)
        {
            var available = r.OnHand - r.Reserved;
            var onOrder = open.Where(o => o.ProductId == r.Id).Sum(o => o.Outstanding);
            var target = r.MaxStockQty ?? r.MinStockQty!.Value;
            var needed = Math.Max(0, target - available - onOrder);
            var pack = r.PurchaseUnitId is { } pu ? r.Units.FirstOrDefault(u => u.UnitId == pu) : null;
            var factor = pack?.FactorToBase ?? 1;
            var packs = needed == 0 ? 0 : Math.Ceiling(needed / factor);
            var supplier = r.DefaultSupplierId is { } sid && suppliers.TryGetValue(sid, out var s) && s.IsActive ? s : null;
            result.Add(new ReorderSuggestionDto(r.Id, r.Sku, r.Name, units[r.BaseUnitId], r.MinStockQty!.Value, r.MaxStockQty, r.OnHand, r.Reserved, available, onOrder,
                packs * factor, pack?.Id, pack is null ? units[r.BaseUnitId] : units[pack.UnitId], factor, packs,
                supplier?.Id, supplier?.Code, supplier?.Name, supplier?.LeadTimeDays,
                open.Where(o => o.ProductId == r.Id).Select(o => o.Number).Distinct().Order().ToList()));
        }
        return result.OrderBy(x => x.SupplierName is null).ThenBy(x => x.SupplierName).ThenBy(x => x.Sku).ToList();
    }

    public async Task<PurchasingSettingsDto> GetSettingsAsync(CancellationToken cancellationToken)
    {
        var company = await db.CompanyProfiles.AsNoTracking().FirstAsync(cancellationToken);
        return new PurchasingSettingsDto(company.LowStockAlertEmails);
    }

    public async Task<PurchasingSettingsDto> UpdateSettingsAsync(UpdatePurchasingSettingsRequest r, CancellationToken cancellationToken)
    {
        var recipients = NormalizeRecipients(r.LowStockAlertEmails);
        var company = await db.CompanyProfiles.FirstAsync(cancellationToken);
        company.SetLowStockAlert(recipients);
        audit.Add("purchasing.settings_updated", "company", company.Id.ToString(), data: new { lowStockAlert = recipients is not null });
        await db.SaveChangesAsync(cancellationToken);
        return new PurchasingSettingsDto(company.LowStockAlertEmails);
    }

    private static string? NormalizeRecipients(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var list = value.Split([',', ';', ' ', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var e in list)
        {
            if (!MailAddress.TryCreate(e, out var parsed) || parsed.Address != e)
            {
                throw new DomainRuleException("purchasing.alert_email_invalid", $"“{e}” is not a valid email address.");
            }
        }
        return string.Join(", ", list.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Queues the daily low-stock email (at most once per company-local day, from <see cref="AlertHour"/>) when recipients are set and
    /// at least one product in an active warehouse needs ordering. Returns true when an email was queued.
    /// </summary>
    public async Task<bool> SendDailyAlertAsync(CancellationToken cancellationToken)
    {
        var company = await db.CompanyProfiles.FirstOrDefaultAsync(cancellationToken);
        if (company?.LowStockAlertEmails is null) return false;
        var zone = TimeZones.FindOrUtc(company.TimeZone);
        var local = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone);
        var today = DateOnly.FromDateTime(local.DateTime);
        if (local.Hour < AlertHour || company.LowStockAlertCheckedOn == today) return false;

        var body = new StringBuilder();
        var toOrder = 0;
        var warehouses = await db.Warehouses.AsNoTracking().Where(w => w.IsActive).OrderBy(w => w.Code).ToListAsync(cancellationToken);
        foreach (var w in warehouses)
        {
            var items = (await ComputeAsync(w.Id, cancellationToken)).Where(s => s.SuggestedQuantity > 0).ToList();
            if (items.Count == 0) continue;
            toOrder += items.Count;
            body.AppendLine(CultureInfo.InvariantCulture, $"{w.Code} · {w.Name}");
            foreach (var s in items)
            {
                body.AppendLine(CultureInfo.InvariantCulture,
                    $"- {s.Sku} {s.ProductName}: available {N(s.Available)} {s.BaseUnitCode} (minimum {N(s.MinStockQty)}, on order {N(s.OnOrder)}). " +
                    $"Suggested: {N(s.SuggestedPurchaseQuantity)} {s.PurchaseUnitCode}{(s.SupplierName is null ? "" : " from " + s.SupplierName)}.");
            }
            body.AppendLine();
        }

        company.MarkLowStockAlertChecked(today);
        if (toOrder > 0)
        {
            body.AppendLine("Create the purchase orders from Purchasing → Reorder in the web app.");
            db.EmailMessages.Add(EmailMessage.Create(company.LowStockAlertEmails, null,
                $"Low stock: {toOrder} product{(toOrder == 1 ? "" : "s")} to reorder", body.ToString(), [], "low_stock", null, clock.GetUtcNow()));
        }
        await db.SaveChangesAsync(cancellationToken);
        return toOrder > 0;
    }

    private static string N(decimal d) => d.ToString("0.###", CultureInfo.InvariantCulture);
}
