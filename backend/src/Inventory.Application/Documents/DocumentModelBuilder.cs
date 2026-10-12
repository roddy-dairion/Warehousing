using System.Globalization;
using Inventory.Application.Common;
using Inventory.Application.Dispatches;
using Inventory.Domain.Dispatches;
using Inventory.Domain.Documents;
using Inventory.Domain.Inventory;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Application.Documents;

/// <summary>
/// Builds the data a document template sees (snake_case keys, values already formatted for printing).
/// The same shape is used for the sample data in the template editor, so <see cref="Describe"/> documents both.
/// </summary>
public sealed class DocumentModelBuilder(IApplicationDbContext db, IFileStorage files)
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <summary>The placeholders available in templates (shown in the editor).</summary>
    public static IReadOnlyList<string> Describe() =>
    [
        "company.name, company.address_lines[], company.phone, company.email, company.website, company.registration_number, company.vat_number, company.footer, company.logo (image data, use in <img src>)",
        "document.title, document.type (PackingSlip | DeliveryNote), document.generated_at",
        "warehouse.code, warehouse.name, warehouse.address_lines[], warehouse.phone, warehouse.email",
        "customer.reference, customer.name, customer.contact, customer.phone, customer.email",
        "ship_to.label, ship_to.address_lines[], ship_to.contact, ship_to.phone",
        "dispatch.number, dispatch.customer_reference, dispatch.requested_date, dispatch.shipped_at, dispatch.carrier, dispatch.tracking_number, dispatch.notes, dispatch.backorder_of, dispatch.is_short, dispatch.backorder_number",
        "lines[]: line_no, sku, name, unit, ordered, shipped, outstanding, lots[] (lot_no, expiry, quantity), lots_text",
        "totals.lines, totals.ordered, totals.shipped",
        "delivery.receiver_name, delivery.delivered_at, delivery.notes, delivery.signature (image data) — delivery note only",
    ];

    public async Task<Dictionary<string, object?>> BuildAsync(Guid dispatchId, DocumentType type, CompanyProfile company, CancellationToken cancellationToken)
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById(company.TimeZone);
        var d = await db.Dispatches.AsNoTracking().Include(x => x.Lines).SingleAsync(x => x.Id == dispatchId, cancellationToken);
        var warehouse = await db.Warehouses.AsNoTracking().SingleAsync(w => w.Id == d.WarehouseId, cancellationToken);
        var customer = await db.Customers.AsNoTracking().SingleAsync(c => c.Id == d.CustomerId, cancellationToken);
        var productIds = d.Lines.Select(l => l.ProductId).ToList();
        var products = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Sku, p.Name, Unit = db.UnitsOfMeasure.Where(u => u.Id == p.BaseUnitId).Select(u => u.Code).First() })
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        // Lots actually shipped, from the DISPATCH movements (negative quantities).
        var shippedLots = await (from m in db.InventoryMovements.AsNoTracking()
                                 where m.SourceType == SourceTypes.Dispatch && m.SourceId == dispatchId && m.Type == MovementType.Dispatch && m.LotId != null
                                 join lot in db.ProductLots on m.LotId equals lot.Id
                                 group m by new { m.ProductId, lot.LotNo, lot.ExpiryDate } into g
                                 select new { g.Key.ProductId, g.Key.LotNo, g.Key.ExpiryDate, Qty = -g.Sum(x => x.Quantity) }).ToListAsync(cancellationToken);

        var backorder = await db.Dispatches.AsNoTracking().Where(x => x.BackorderOfId == dispatchId).Select(x => x.Number).FirstOrDefaultAsync(cancellationToken);
        var parent = d.BackorderOfId is null ? null : await db.Dispatches.Where(x => x.Id == d.BackorderOfId).Select(x => x.Number).SingleAsync(cancellationToken);

        var lines = d.Lines.OrderBy(l => l.LineNo).Select(l =>
        {
            var p = products[l.ProductId];
            var lots = shippedLots.Where(x => x.ProductId == l.ProductId).OrderBy(x => x.ExpiryDate).ThenBy(x => x.LotNo)
                .Select(x => (object?)new Dictionary<string, object?> { ["lot_no"] = x.LotNo, ["expiry"] = FormatDate(x.ExpiryDate), ["quantity"] = Qty(x.Qty) }).ToList();
            return (object?)new Dictionary<string, object?>
            {
                ["line_no"] = l.LineNo,
                ["sku"] = p.Sku,
                ["name"] = p.Name,
                ["unit"] = p.Unit,
                ["ordered"] = Qty(l.Quantity),
                ["shipped"] = Qty(l.QuantityShipped),
                ["outstanding"] = Qty(l.Quantity - l.QuantityShipped),
                ["lots"] = lots,
                ["lots_text"] = string.Join(", ", shippedLots.Where(x => x.ProductId == l.ProductId).Select(x => $"{x.LotNo} ({Qty(x.Qty)})")),
                ["notes"] = l.Notes,
            };
        }).ToList();

        var model = Common(company, type, tz, await LogoAsync(company, cancellationToken));
        model["warehouse"] = new Dictionary<string, object?>
        {
            ["code"] = warehouse.Code, ["name"] = warehouse.Name,
            ["address_lines"] = Lines(warehouse.AddressLine1, warehouse.AddressLine2, warehouse.City, warehouse.Country),
            ["phone"] = warehouse.Phone, ["email"] = warehouse.Email,
        };
        model["customer"] = new Dictionary<string, object?>
        {
            ["reference"] = customer.Reference, ["name"] = customer.CompanyName, ["contact"] = customer.ContactName, ["phone"] = customer.Phone, ["email"] = customer.Email,
        };
        var a = d.ShipTo;
        model["ship_to"] = new Dictionary<string, object?>
        {
            ["label"] = a?.Label, ["address_lines"] = Lines(a?.Line1, a?.Line2, a?.City, a?.Country), ["contact"] = a?.ContactName, ["phone"] = a?.Phone,
        };
        model["dispatch"] = new Dictionary<string, object?>
        {
            ["number"] = d.Number,
            ["customer_reference"] = d.CustomerReference,
            ["requested_date"] = FormatDate(d.RequestedDate),
            ["shipped_at"] = FormatTime(d.ShippedAt, tz),
            ["carrier"] = d.Carrier,
            ["tracking_number"] = d.TrackingNumber,
            ["notes"] = d.Notes,
            ["backorder_of"] = parent,
            ["is_short"] = d.ShortDecision is not null,
            ["backorder_number"] = backorder,
        };
        model["lines"] = lines;
        model["totals"] = new Dictionary<string, object?>
        {
            ["lines"] = d.Lines.Count, ["ordered"] = Qty(d.Lines.Sum(l => l.Quantity)), ["shipped"] = Qty(d.Lines.Sum(l => l.QuantityShipped)),
        };

        if (type == DocumentType.DeliveryNote)
        {
            var pod = await db.ProofsOfDelivery.AsNoTracking().SingleOrDefaultAsync(p => p.DispatchId == dispatchId, cancellationToken)
                ?? throw new InvalidOperationException("A delivery note needs the proof of delivery.");
            model["delivery"] = new Dictionary<string, object?>
            {
                ["receiver_name"] = pod.ReceiverName,
                ["delivered_at"] = FormatTime(pod.DeliveredAt, tz),
                ["notes"] = pod.Notes,
                ["signature"] = await DataUriAsync(pod.SignatureFileId, cancellationToken),
            };
        }
        return model;
    }

    /// <summary>Example data for previews and template validation.</summary>
    public async Task<Dictionary<string, object?>> SampleAsync(DocumentType type, CompanyProfile company, CancellationToken cancellationToken)
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById(company.TimeZone);
        var model = Common(company, type, tz, await LogoAsync(company, cancellationToken));
        model["warehouse"] = new Dictionary<string, object?> { ["code"] = "MAIN", ["name"] = "Main warehouse", ["address_lines"] = new List<object?> { "1 Warehouse Road", "Port Louis" }, ["phone"] = "+230 000 0000", ["email"] = null };
        model["customer"] = new Dictionary<string, object?> { ["reference"] = "CUST-001", ["name"] = "Example Customer Ltd", ["contact"] = "Jane Doe", ["phone"] = "+230 111 1111", ["email"] = "orders@example.com" };
        model["ship_to"] = new Dictionary<string, object?> { ["label"] = "Head office", ["address_lines"] = new List<object?> { "12 Example Street", "Ebene", "Mauritius" }, ["contact"] = "Jane Doe", ["phone"] = "+230 111 1111" };
        model["dispatch"] = new Dictionary<string, object?>
        {
            ["number"] = "DSP-MAIN-2026-000123", ["customer_reference"] = "PO-4567", ["requested_date"] = FormatDate(DateOnly.FromDateTime(DateTime.UtcNow)),
            ["shipped_at"] = FormatTime(DateTimeOffset.UtcNow, tz), ["carrier"] = "Own van", ["tracking_number"] = "VAN-2", ["notes"] = "Deliver to the back entrance.",
            ["backorder_of"] = null, ["is_short"] = true, ["backorder_number"] = "DSP-MAIN-2026-000124",
        };
        model["lines"] = new List<object?>
        {
            new Dictionary<string, object?> { ["line_no"] = 1, ["sku"] = "SOAP-100", ["name"] = "Soap bar 100 g", ["unit"] = "EA", ["ordered"] = "48", ["shipped"] = "48", ["outstanding"] = "0",
                ["lots"] = new List<object?> { new Dictionary<string, object?> { ["lot_no"] = "L2401", ["expiry"] = "31.12.2027", ["quantity"] = "48" } }, ["lots_text"] = "L2401 (48)", ["notes"] = null },
            new Dictionary<string, object?> { ["line_no"] = 2, ["sku"] = "OIL-1L", ["name"] = "Sunflower oil 1 L", ["unit"] = "EA", ["ordered"] = "24", ["shipped"] = "20", ["outstanding"] = "4",
                ["lots"] = new List<object?>(), ["lots_text"] = "", ["notes"] = null },
        };
        model["totals"] = new Dictionary<string, object?> { ["lines"] = 2, ["ordered"] = "72", ["shipped"] = "68" };
        if (type == DocumentType.DeliveryNote)
        {
            model["delivery"] = new Dictionary<string, object?>
            {
                ["receiver_name"] = "John Smith", ["delivered_at"] = FormatTime(DateTimeOffset.UtcNow, tz), ["notes"] = null,
                ["signature"] = "data:image/svg+xml;base64," + Convert.ToBase64String("<svg xmlns='http://www.w3.org/2000/svg' width='240' height='80'><path d='M10 60 C 40 10, 70 10, 90 50 S 150 80, 230 20' stroke='black' fill='none' stroke-width='3'/></svg>"u8.ToArray()),
            };
        }
        return model;
    }

    /// <summary>Model for the delivery-note email text.</summary>
    public static Dictionary<string, object?> EmailModel(Dictionary<string, object?> documentModel) => documentModel;

    private static Dictionary<string, object?> Common(CompanyProfile c, DocumentType type, TimeZoneInfo tz, string? logo) => new()
    {
        ["company"] = new Dictionary<string, object?>
        {
            ["name"] = c.Name, ["address_lines"] = Lines(c.AddressLine1, c.AddressLine2, c.City, c.Country),
            ["phone"] = c.Phone, ["email"] = c.Email, ["website"] = c.Website, ["registration_number"] = c.RegistrationNumber, ["vat_number"] = c.VatNumber,
            ["footer"] = c.FooterText, ["logo"] = logo,
        },
        ["document"] = new Dictionary<string, object?>
        {
            ["type"] = type.ToString(),
            ["title"] = type == DocumentType.DeliveryNote ? "Delivery note" : "Packing slip",
            ["generated_at"] = FormatTime(DateTimeOffset.UtcNow, tz),
        },
    };

    private async Task<string?> LogoAsync(CompanyProfile company, CancellationToken cancellationToken) =>
        company.LogoFileId is { } id ? await DataUriAsync(id, cancellationToken) : null;

    private async Task<string?> DataUriAsync(Guid fileId, CancellationToken cancellationToken)
    {
        var file = await db.StoredFiles.AsNoTracking().SingleOrDefaultAsync(f => f.Id == fileId, cancellationToken);
        if (file is null) return null;
        await using var stream = await files.OpenReadAsync(file.StorageKey, cancellationToken);
        if (stream is null) return null;
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, cancellationToken);
        return $"data:{file.ContentType};base64,{Convert.ToBase64String(ms.ToArray())}";
    }

    private static List<object?> Lines(params string?[] parts) => parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => (object?)p).ToList();

    private static string Qty(decimal q) => q.ToString("0.###", Invariant);

    private static string? FormatDate(DateOnly? d) => d?.ToString("dd.MM.yyyy", Invariant);

    private static string? FormatTime(DateTimeOffset? t, TimeZoneInfo tz) =>
        t is null ? null : TimeZoneInfo.ConvertTime(t.Value, tz).ToString("dd.MM.yyyy HH:mm", Invariant);
}
