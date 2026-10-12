using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using Inventory.Application.Common;
using Inventory.Domain.Dispatches;
using Inventory.Domain.Inventory;
using Inventory.Domain.Warehouses;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Application.Reporting;

public static class ReportKinds
{
    public const string StockOnHand = "stock-on-hand";
    public const string StockSummary = "stock-summary";
    public const string Movements = "movements";
    public const string GoodsReceived = "goods-received";
    public const string Adjustments = "adjustments";
    public const string Transfers = "transfers";
    public const string Dispatches = "dispatches";
    public const string DispatchDiscrepancies = "dispatch-discrepancies";

    public static IReadOnlyList<ReportInfoDto> All { get; } =
    [
        new(StockOnHand, "Stock on hand by bin and lot", false),
        new(StockSummary, "Stock by product (reserved, available, low stock)", false),
        new(Movements, "Stock movement history", true),
        new(GoodsReceived, "Goods received", true),
        new(Adjustments, "Stock adjustments", true),
        new(Transfers, "Bin moves", true),
        new(Dispatches, "Dispatches by status and date", true),
        new(DispatchDiscrepancies, "Short shipments and picking discrepancies", true),
    ];
}

public sealed record ReportInfoDto(string Kind, string Title, bool UsesDates);

public sealed record ReportQuery(Guid WarehouseId, DateOnly? From, DateOnly? To, DispatchStatus? Status);

/// <summary>A report as rows of formatted text, ready for a table preview or a CSV download.</summary>
public sealed record ReportResult(string Kind, string Title, IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<string?>> Rows, bool Truncated);

/// <summary>
/// Reports for one warehouse the user can access (the same scope as the screens). Dates are inclusive and compared in UTC.
/// </summary>
public sealed class ReportService(IApplicationDbContext db, IWarehouseAccess access, IUserDirectory users)
{
    public const int PreviewRows = 500;
    public const int MaxRows = 50_000;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public async Task<ReportResult> RunAsync(string kind, ReportQuery q, int limit, CancellationToken cancellationToken)
    {
        await access.EnsureCanAccessAsync(q.WarehouseId, cancellationToken);
        var info = ReportKinds.All.FirstOrDefault(r => r.Kind == kind) ?? throw new NotFoundException("Unknown report.");
        var take = Math.Clamp(limit, 1, MaxRows) + 1; // one extra row tells us the result was cut
        var (fromAt, toAt) = Range(q);

        (string[] columns, List<string?[]> rows) = kind switch
        {
            ReportKinds.StockOnHand => await StockOnHandAsync(q.WarehouseId, take, cancellationToken),
            ReportKinds.StockSummary => await StockSummaryAsync(q.WarehouseId, take, cancellationToken),
            ReportKinds.Movements => await MovementsAsync(q.WarehouseId, fromAt, toAt, take, cancellationToken),
            ReportKinds.GoodsReceived => await ReceivedAsync(q.WarehouseId, q.From, q.To, take, cancellationToken),
            ReportKinds.Adjustments => await AdjustmentsAsync(q.WarehouseId, fromAt, toAt, take, cancellationToken),
            ReportKinds.Transfers => await TransfersAsync(q.WarehouseId, fromAt, toAt, take, cancellationToken),
            ReportKinds.Dispatches => await DispatchesAsync(q, take, cancellationToken),
            _ => await DiscrepanciesAsync(q.WarehouseId, fromAt, toAt, take, cancellationToken),
        };
        var truncated = rows.Count >= take;
        if (truncated) rows.RemoveAt(rows.Count - 1);
        return new ReportResult(kind, info.Title, columns, rows, truncated);
    }

    public static byte[] ToCsv(ReportResult report)
    {
        using var ms = new MemoryStream();
        using (var writer = new StreamWriter(ms, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)))
        using (var csv = new CsvWriter(writer, new CsvConfiguration(Inv)))
        {
            foreach (var c in report.Columns) csv.WriteField(c);
            csv.NextRecord();
            foreach (var row in report.Rows)
            {
                foreach (var v in row) csv.WriteField(SafeCell(v));
                csv.NextRecord();
            }
        }
        return ms.ToArray();
    }

    /// <summary>
    /// CSV/formula injection guard: text typed by users (product names, notes, lots…) that starts with = + - @ or a control
    /// character would run as a formula when the file is opened in Excel. Such cells get a leading apostrophe.
    /// Numbers (e.g. "-24") are left as they are.
    /// </summary>
    public static string? SafeCell(string? value)
    {
        if (string.IsNullOrEmpty(value) || value[0] is not ('=' or '+' or '-' or '@' or '\t' or '\r')) return value;
        return decimal.TryParse(value, NumberStyles.Number, Inv, out _) ? value : "'" + value;
    }

    private static (DateTimeOffset? From, DateTimeOffset? To) Range(ReportQuery q) => (
        q.From is { } f ? new DateTimeOffset(f.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) : null,
        q.To is { } t ? new DateTimeOffset(t.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) : null);

    private static string N(decimal v) => v.ToString("0.###", Inv);
    private static string? D(DateOnly? d) => d?.ToString("yyyy-MM-dd", Inv);
    private static string? T(DateTimeOffset? t) => t?.UtcDateTime.ToString("yyyy-MM-dd HH:mm", Inv);

    private async Task<(string[], List<string?[]>)> StockOnHandAsync(Guid wh, int take, CancellationToken ct)
    {
        var rows = await (from b in db.InventoryBalances.AsNoTracking()
                          where b.WarehouseId == wh && b.OnHand > 0
                          join p in db.Products on b.ProductId equals p.Id
                          join loc in db.WarehouseLocations on b.LocationId equals loc.Id
                          from lot in db.ProductLots.Where(l => l.Id == b.LotId).DefaultIfEmpty()
                          orderby p.Sku, loc.Code
                          select new
                          {
                              p.Sku, p.Name, Unit = db.UnitsOfMeasure.Where(u => u.Id == p.BaseUnitId).Select(u => u.Code).First(), Bin = loc.Code, loc.Type,
                              LotNo = lot == null ? null : lot.LotNo, Expiry = lot == null ? null : lot.ExpiryDate, LotStatus = lot == null ? (LotStatus?)null : lot.Status,
                              b.OnHand, b.Reserved, b.Available,
                          }).Take(take).ToListAsync(ct);
        return (["SKU", "Product", "Bin", "Bin type", "Lot", "Expiry", "Lot status", "On hand", "Reserved", "Available", "Unit"],
            rows.Select(r => new string?[] { r.Sku, r.Name, r.Bin, r.Type.ToString(), r.LotNo, D(r.Expiry), r.LotStatus?.ToString(), N(r.OnHand), N(r.Reserved), N(r.Available), r.Unit }).ToList());
    }

    private async Task<(string[], List<string?[]>)> StockSummaryAsync(Guid wh, int take, CancellationToken ct)
    {
        var rows = await (from p in db.Products.AsNoTracking()
                          let onHand = db.InventoryBalances.Where(b => b.WarehouseId == wh && b.ProductId == p.Id).Sum(b => (decimal?)b.OnHand) ?? 0
                          let reserved = db.InventoryBalances.Where(b => b.WarehouseId == wh && b.ProductId == p.Id).Sum(b => (decimal?)b.Reserved) ?? 0
                          where onHand > 0 || (p.MinStockQty != null && p.IsActive)
                          orderby p.Sku
                          select new
                          {
                              p.Sku, p.Name, Unit = db.UnitsOfMeasure.Where(u => u.Id == p.BaseUnitId).Select(u => u.Code).First(),
                              Category = db.ProductCategories.Where(c => c.Id == p.CategoryId).Select(c => c.Name).FirstOrDefault(),
                              onHand, reserved, p.MinStockQty, p.IsActive,
                          }).Take(take).ToListAsync(ct);
        return (["SKU", "Product", "Category", "On hand", "Reserved", "Available", "Min. stock", "Low stock", "Unit", "Active"],
            rows.Select(r => new string?[]
            {
                r.Sku, r.Name, r.Category, N(r.onHand), N(r.reserved), N(r.onHand - r.reserved), r.MinStockQty is { } m ? N(m) : null,
                r.MinStockQty is { } min && r.onHand - r.reserved < min ? "yes" : "no", r.Unit, r.IsActive ? "yes" : "no",
            }).ToList());
    }

    private async Task<(string[], List<string?[]>)> MovementsAsync(Guid wh, DateTimeOffset? fromAt, DateTimeOffset? toAt, int take, CancellationToken ct)
    {
        var rows = await (from m in db.InventoryMovements.AsNoTracking()
                          where m.WarehouseId == wh && (fromAt == null || m.OccurredAt >= fromAt) && (toAt == null || m.OccurredAt < toAt)
                          join p in db.Products on m.ProductId equals p.Id
                          join loc in db.WarehouseLocations on m.LocationId equals loc.Id
                          from lot in db.ProductLots.Where(l => l.Id == m.LotId).DefaultIfEmpty()
                          orderby m.OccurredAt, m.Sequence
                          select new { m.OccurredAt, m.Type, p.Sku, p.Name, Bin = loc.Code, LotNo = lot == null ? null : lot.LotNo, m.Quantity, m.OnHandAfter, m.SourceNumber, m.Reason, m.CreatedBy })
            .Take(take).ToListAsync(ct);
        var names = await users.NamesAsync(rows.Select(r => r.CreatedBy), ct);
        return (["Date (UTC)", "Type", "SKU", "Product", "Bin", "Lot", "Quantity", "On hand after", "Document", "Reason", "By"],
            rows.Select(r => new string?[] { T(r.OccurredAt), r.Type.ToString(), r.Sku, r.Name, r.Bin, r.LotNo, N(r.Quantity), N(r.OnHandAfter), r.SourceNumber, r.Reason, names.NameOf(r.CreatedBy) }).ToList());
    }

    private async Task<(string[], List<string?[]>)> ReceivedAsync(Guid wh, DateOnly? fromAt, DateOnly? toAt, int take, CancellationToken ct)
    {
        var rows = await (from r in db.GoodsReceipts.AsNoTracking()
                          where r.WarehouseId == wh && r.Status != ReceiptStatus.Draft && r.Status != ReceiptStatus.Cancelled
                                && (fromAt == null || r.ReceiptDate >= fromAt) && (toAt == null || r.ReceiptDate <= toAt)
                          from l in r.Lines
                          join p in db.Products on l.ProductId equals p.Id
                          join loc in db.WarehouseLocations on l.LocationId equals loc.Id
                          orderby r.ReceiptDate, r.Number, l.LineNo
                          select new
                          {
                              r.Number, r.ReceiptDate, r.SupplierName, r.SupplierReference, r.Status, p.Sku, p.Name, l.Quantity, l.ExpectedQuantity, l.LotNo, l.ExpiryDate, Bin = loc.Code,
                              Unit = db.UnitsOfMeasure.Where(u => u.Id == p.BaseUnitId).Select(u => u.Code).First(), r.PostedBy,
                          }).Take(take).ToListAsync(ct);
        var names = await users.NamesAsync(rows.Select(r => r.PostedBy), ct);
        return (["Receipt", "Date", "Status", "Supplier", "Supplier ref.", "SKU", "Product", "Received", "Expected", "Unit", "Lot", "Expiry", "Bin", "Posted by"],
            rows.Select(r => new string?[]
            {
                r.Number, D(r.ReceiptDate), r.Status.ToString(), r.SupplierName, r.SupplierReference, r.Sku, r.Name, N(r.Quantity),
                r.ExpectedQuantity is { } e ? N(e) : null, r.Unit, r.LotNo, D(r.ExpiryDate), r.Bin, names.NameOf(r.PostedBy),
            }).ToList());
    }

    private async Task<(string[], List<string?[]>)> AdjustmentsAsync(Guid wh, DateTimeOffset? fromAt, DateTimeOffset? toAt, int take, CancellationToken ct)
    {
        var rows = await (from a in db.StockAdjustments.AsNoTracking()
                          where a.WarehouseId == wh && a.Status == AdjustmentStatus.Posted && (fromAt == null || a.DecidedAt >= fromAt) && (toAt == null || a.DecidedAt < toAt)
                          from l in a.Lines
                          join p in db.Products on l.ProductId equals p.Id
                          join loc in db.WarehouseLocations on l.LocationId equals loc.Id
                          orderby a.DecidedAt, a.Number, l.LineNo
                          select new { a.Number, a.DecidedAt, a.Reason, a.Notes, p.Sku, p.Name, Bin = loc.Code, l.LotNo, l.QuantityDelta, a.SubmittedBy, a.DecidedBy })
            .Take(take).ToListAsync(ct);
        var names = await users.NamesAsync(rows.SelectMany(r => new[] { r.SubmittedBy, r.DecidedBy }), ct);
        return (["Adjustment", "Posted (UTC)", "Reason", "SKU", "Product", "Bin", "Lot", "Change", "Requested by", "Approved by", "Notes"],
            rows.Select(r => new string?[] { r.Number, T(r.DecidedAt), r.Reason.ToString(), r.Sku, r.Name, r.Bin, r.LotNo, N(r.QuantityDelta), names.NameOf(r.SubmittedBy), names.NameOf(r.DecidedBy), r.Notes }).ToList());
    }

    private async Task<(string[], List<string?[]>)> TransfersAsync(Guid wh, DateTimeOffset? fromAt, DateTimeOffset? toAt, int take, CancellationToken ct)
    {
        var rows = await (from t in db.StockTransfers.AsNoTracking()
                          where (t.FromWarehouseId == wh || t.ToWarehouseId == wh) && (fromAt == null || t.PostedAt >= fromAt) && (toAt == null || t.PostedAt < toAt)
                          from l in t.Lines
                          join p in db.Products on l.ProductId equals p.Id
                          join src in db.WarehouseLocations on l.FromLocationId equals src.Id
                          join dst in db.WarehouseLocations on l.ToLocationId equals dst.Id
                          from lot in db.ProductLots.Where(x => x.Id == l.LotId).DefaultIfEmpty()
                          orderby t.PostedAt, t.Number, l.LineNo
                          select new { t.Number, t.PostedAt, t.Type, p.Sku, p.Name, LotNo = lot == null ? null : lot.LotNo, From = src.Code, To = dst.Code, l.Quantity, t.PostedBy })
            .Take(take).ToListAsync(ct);
        var names = await users.NamesAsync(rows.Select(r => r.PostedBy), ct);
        return (["Move", "Date (UTC)", "Type", "SKU", "Product", "Lot", "From bin", "To bin", "Quantity", "By"],
            rows.Select(r => new string?[] { r.Number, T(r.PostedAt), r.Type.ToString(), r.Sku, r.Name, r.LotNo, r.From, r.To, N(r.Quantity), names.NameOf(r.PostedBy) }).ToList());
    }

    private async Task<(string[], List<string?[]>)> DispatchesAsync(ReportQuery q, int take, CancellationToken ct)
    {
        var rows = await db.Dispatches.AsNoTracking()
            .Where(d => d.WarehouseId == q.WarehouseId && (q.Status == null || d.Status == q.Status)
                        && (q.From == null || d.RequestedDate >= q.From) && (q.To == null || d.RequestedDate <= q.To))
            .OrderBy(d => d.RequestedDate).ThenBy(d => d.Number)
            .Select(d => new
            {
                d.Number, d.RequestedDate, d.Status, d.CustomerReference, d.Carrier, d.TrackingNumber, d.ShippedAt, d.DeliveredAt, d.ShortDecision,
                Customer = db.Customers.Where(c => c.Id == d.CustomerId).Select(c => c.CompanyName).First(),
                Lines = d.Lines.Count, Requested = d.Lines.Sum(l => l.Quantity), Shipped = d.Lines.Sum(l => l.QuantityShipped),
                BackorderOf = db.Dispatches.Where(p => p.Id == d.BackorderOfId).Select(p => p.Number).FirstOrDefault(),
            }).Take(take).ToListAsync(ct);
        return (["Dispatch", "Requested", "Status", "Customer", "Customer ref.", "Lines", "Requested qty", "Shipped qty", "Short", "Backorder of", "Shipped (UTC)", "Delivered (UTC)", "Carrier", "Tracking"],
            rows.Select(r => new string?[]
            {
                r.Number, D(r.RequestedDate), r.Status.ToString(), r.Customer, r.CustomerReference, r.Lines.ToString(Inv), N(r.Requested), N(r.Shipped),
                r.ShortDecision?.ToString(), r.BackorderOf, T(r.ShippedAt), T(r.DeliveredAt), r.Carrier, r.TrackingNumber,
            }).ToList());
    }

    private async Task<(string[], List<string?[]>)> DiscrepanciesAsync(Guid wh, DateTimeOffset? fromAt, DateTimeOffset? toAt, int take, CancellationToken ct)
    {
        // Short-shipped lines …
        var shortLines = await (from d in db.Dispatches.AsNoTracking()
                                where d.WarehouseId == wh && d.ShortDecision != null && (fromAt == null || d.ShippedAt >= fromAt) && (toAt == null || d.ShippedAt < toAt)
                                from l in d.Lines
                                where l.QuantityShipped < l.Quantity
                                join p in db.Products on l.ProductId equals p.Id
                                select new
                                {
                                    d.Number, At = d.ShippedAt, Customer = db.Customers.Where(c => c.Id == d.CustomerId).Select(c => c.CompanyName).First(),
                                    p.Sku, p.Name, l.Quantity, l.QuantityShipped, Decision = d.ShortDecision,
                                    Backorder = db.Dispatches.Where(b => b.BackorderOfId == d.Id).Select(b => b.Number).FirstOrDefault(),
                                }).Take(take).ToListAsync(ct);
        // … and problems reported while picking.
        var reported = await (from x in db.DispatchDiscrepancies.AsNoTracking()
                              join d in db.Dispatches on x.DispatchId equals d.Id
                              where d.WarehouseId == wh && (fromAt == null || x.At >= fromAt) && (toAt == null || x.At < toAt)
                              from p in db.Products.Where(p => p.Id == x.ProductId).DefaultIfEmpty()
                              select new
                              {
                                  d.Number, x.At, Customer = db.Customers.Where(c => c.Id == d.CustomerId).Select(c => c.CompanyName).First(),
                                  Sku = p == null ? null : p.Sku, Name = p == null ? null : p.Name, x.Type, x.Quantity, x.Note, x.Barcode, x.UserId,
                              }).Take(take).ToListAsync(ct);
        var names = await users.NamesAsync(reported.Select(r => r.UserId), ct);

        var rows = shortLines.Select(r => (At: r.At, Row: new string?[]
            {
                r.Number, T(r.At), "Short shipment", r.Customer, r.Sku, r.Name, N(r.Quantity), N(r.QuantityShipped), N(r.Quantity - r.QuantityShipped),
                r.Decision?.ToString(), r.Backorder, null, null,
            }))
            .Concat(reported.Select(r => (At: (DateTimeOffset?)r.At, Row: new string?[]
            {
                r.Number, T(r.At), $"Reported: {r.Type}", r.Customer, r.Sku ?? r.Barcode, r.Name, null, null, r.Quantity is { } q ? N(q) : null,
                null, null, r.Note, names.NameOf(r.UserId),
            })))
            .OrderBy(x => x.At).Select(x => x.Row).Take(take).ToList();
        return (["Dispatch", "Date (UTC)", "Kind", "Customer", "SKU / barcode", "Product", "Requested", "Shipped", "Short / qty", "Decision", "Backorder", "Note", "Reported by"], rows);
    }
}
