using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using Inventory.Application.Common;
using Inventory.Domain.Common;
using Inventory.Domain.Customers;
using Inventory.Domain.Products;
using Inventory.Domain.Warehouses;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Application.Imports;

public enum ImportKind
{
    Products,
    Customers,
    Locations,
}

public sealed record ImportError(int Row, string? Column, string Message);

/// <param name="Applied">True when the changes were saved (no errors and not a dry run).</param>
public sealed record ImportResultDto(ImportKind Kind, bool DryRun, bool Applied, int TotalRows, int Created, int Updated, IReadOnlyList<ImportError> Errors);

/// <summary>
/// CSV import of master data. The whole file is validated first; changes are saved only when
/// every row is valid (all or nothing). Blank optional cells leave existing values unchanged.
/// Comma or semicolon separators are both accepted (Excel in French locale uses ';').
/// </summary>
public sealed class ImportService(IApplicationDbContext db, IWarehouseAccess access, IAuditWriter audit)
{
    public const int MaxRows = 10_000;

    public static readonly IReadOnlyDictionary<ImportKind, string> Templates = new Dictionary<ImportKind, string>
    {
        [ImportKind.Products] =
            "sku,name,description,category_code,base_unit,min_stock,lot_tracked,has_expiry,shelf_life_days,barcode,pack_unit,pack_factor,pack_barcode,active\n" +
            "SOAP-100,Soap bar 100 g,Lavender,HYGIENE,EA,24,no,no,,3017620422003,CTN,48,13017620422000,yes\n",
        [ImportKind.Customers] =
            "reference,company_name,contact_name,phone,email,billing_line1,billing_line2,billing_city,billing_country,notes,delivery_label,delivery_line1,delivery_line2,delivery_city,delivery_country,delivery_contact,delivery_phone,active\n" +
            "CUST-001,Example Ltd,Jane Doe,+230 5 000 0000,orders@example.mu,1 Royal Road,,Port Louis,Mauritius,,Head office,1 Royal Road,,Port Louis,Mauritius,Jane Doe,+230 5 000 0000,yes\n",
        [ImportKind.Locations] =
            "warehouse_code,code,type,zone,barcode,pickable,active\n" +
            "MAIN,A-01-01,storage,A,,yes,yes\n",
    };

    private static readonly string[] RequiredProducts = ["sku", "name", "base_unit"];
    private static readonly string[] RequiredCustomers = ["reference", "company_name"];
    private static readonly string[] RequiredLocations = ["warehouse_code", "code"];

    public async Task<ImportResultDto> ImportAsync(ImportKind kind, Stream content, bool dryRun, CancellationToken cancellationToken)
    {
        var errors = new List<ImportError>();
        var rows = await ReadAsync(content, kind, errors, cancellationToken);
        var result = new Counter();

        if (errors.Count == 0)
        {
            switch (kind)
            {
                case ImportKind.Products: await ImportProductsAsync(rows, errors, result, cancellationToken); break;
                case ImportKind.Customers: await ImportCustomersAsync(rows, errors, result, cancellationToken); break;
                case ImportKind.Locations: await ImportLocationsAsync(rows, errors, result, cancellationToken); break;
            }
        }

        var apply = errors.Count == 0 && !dryRun;
        if (apply)
        {
            audit.Add($"import.{kind.ToString().ToLowerInvariant()}", data: new { rows = rows.Count, result.Created, result.Updated });
            await db.SaveChangesAsync(cancellationToken);
        }
        return new ImportResultDto(kind, dryRun, apply, rows.Count, result.Created, result.Updated, errors.Take(500).ToList());
    }

    // ---- Products ---------------------------------------------------------------------------

    private async Task ImportProductsAsync(List<Row> rows, List<ImportError> errors, Counter result, CancellationToken ct)
    {
        var skus = rows.Select(r => r.Get("sku")?.ToUpperInvariant()).OfType<string>().Distinct().ToList();
        var products = await db.Products.Include(p => p.Units).Include(p => p.Barcodes).Where(p => skus.Contains(p.Sku)).ToDictionaryAsync(p => p.Sku, ct);
        var units = await db.UnitsOfMeasure.AsNoTracking().Where(u => u.IsActive).ToDictionaryAsync(u => u.Code, ct);
        var categories = await db.ProductCategories.AsNoTracking().Where(c => c.IsActive).ToDictionaryAsync(c => c.Code, c => c.Id, ct);

        var fileBarcodes = new Dictionary<string, string>(); // barcode -> sku, to catch duplicates inside the file
        var allCodes = rows.SelectMany(r => new[] { r.Get("barcode"), r.Get("pack_barcode") }).OfType<string>().Distinct().ToList();
        var takenByOthers = await db.ProductBarcodes.AsNoTracking().Where(b => allCodes.Contains(b.Value))
            .Select(b => new { b.Value, Sku = db.Products.Where(p => p.Id == b.ProductId).Select(p => p.Sku).First() }).ToListAsync(ct);
        var takenByBins = await db.WarehouseLocations.AsNoTracking().Where(l => l.Barcode != null && allCodes.Contains(l.Barcode)).Select(l => l.Barcode!).ToListAsync(ct);

        foreach (var row in rows)
        {
            await RowAsync(row, errors, () =>
            {
                var sku = row.Required("sku").ToUpperInvariant();
                var baseUnit = Unit(row.Required("base_unit"), units, "base_unit");
                var isNew = !products.TryGetValue(sku, out var product);
                if (isNew)
                {
                    product = Product.Create(sku, row.Required("name"), baseUnit.Id);
                    db.Products.Add(product);
                    products[product.Sku] = product;
                    result.Created++;
                }
                else
                {
                    product!.Rename(row.Required("name"));
                    if (product.BaseUnitId != baseUnit.Id) product.ChangeBaseUnit(baseUnit.Id);
                    result.Updated++;
                }

                var categoryCode = row.Get("category_code");
                Guid? categoryId = categoryCode is null ? product!.CategoryId
                    : categories.TryGetValue(categoryCode.ToUpperInvariant(), out var cid) ? cid
                    : throw new RowException("category_code", $"Unknown or inactive category {categoryCode}.");
                product!.Describe(row.Get("description") ?? product.Description, categoryId);

                var minStock = row.Decimal("min_stock") ?? product.MinStockQty;
                if (minStock is { } m && decimal.Round(m, baseUnit.Decimals) != m)
                {
                    throw new RowException("min_stock", $"At most {baseUnit.Decimals} decimals for unit {baseUnit.Code}.");
                }
                product.SetStockRules(minStock, row.Bool("lot_tracked") ?? product.IsLotTracked, row.Bool("has_expiry") ?? product.HasExpiry, row.Int("shelf_life_days") ?? product.ShelfLifeDays);

                Guid? packUnitId = null;
                if (row.Get("pack_unit") is { } packCode)
                {
                    var pack = Unit(packCode, units, "pack_unit");
                    var factor = row.Decimal("pack_factor") ?? throw new RowException("pack_factor", "Enter the pack size (e.g. 12).");
                    var existing = product.Units.SingleOrDefault(u => u.UnitId == pack.Id);
                    if (existing is null) product.AddUnit(pack.Id, factor); else product.ChangeUnitFactor(existing.Id, factor);
                    packUnitId = product.Units.Single(u => u.UnitId == pack.Id).Id;
                }

                AddBarcode(product, row.Get("barcode"), null, "barcode");
                if (row.Get("pack_barcode") is { } packBarcode)
                {
                    if (packUnitId is null) throw new RowException("pack_barcode", "A pack barcode needs pack_unit and pack_factor.");
                    AddBarcode(product, packBarcode, packUnitId, "pack_barcode");
                }

                if (row.Bool("active") is { } active)
                {
                    if (active) product.Activate(); else product.Deactivate();
                }
            });
        }

        void AddBarcode(Product product, string? value, Guid? productUnitId, string column)
        {
            if (value is null) return;
            var code = Barcodes.Normalize(value);
            if (product.Barcodes.Any(b => b.Value == code)) return;
            if (fileBarcodes.TryGetValue(code, out var otherSku) && otherSku != product.Sku)
            {
                throw new RowException(column, $"Barcode {code} appears more than once in the file ({otherSku}).");
            }
            var owner = takenByOthers.FirstOrDefault(t => t.Value == code);
            if (owner is not null && owner.Sku != product.Sku) throw new RowException(column, $"Barcode {code} is already used by product {owner.Sku}.");
            if (takenByBins.Contains(code)) throw new RowException(column, $"Barcode {code} is already used by a bin.");
            fileBarcodes[code] = product.Sku;
            product.AddBarcode(code, productUnitId);
        }
    }

    private static UnitOfMeasure Unit(string code, Dictionary<string, UnitOfMeasure> units, string column) =>
        units.TryGetValue(code.ToUpperInvariant(), out var unit) ? unit : throw new RowException(column, $"Unknown or inactive unit {code}.");

    // ---- Customers --------------------------------------------------------------------------

    private async Task ImportCustomersAsync(List<Row> rows, List<ImportError> errors, Counter result, CancellationToken ct)
    {
        var refs = rows.Select(r => r.Get("reference")?.ToUpperInvariant()).OfType<string>().Distinct().ToList();
        var customers = await db.Customers.Include(c => c.Addresses).Where(c => refs.Contains(c.Reference)).ToDictionaryAsync(c => c.Reference, ct);
        var seen = new HashSet<string>();

        foreach (var row in rows)
        {
            await RowAsync(row, errors, () =>
            {
                var reference = row.Required("reference").ToUpperInvariant();
                if (!customers.TryGetValue(reference, out var customer))
                {
                    customer = Customer.Create(reference, row.Required("company_name"));
                    db.Customers.Add(customer);
                    customers[customer.Reference] = customer;
                    result.Created++;
                }
                else
                {
                    customer.Rename(row.Required("company_name"));
                    if (seen.Add(reference) && db.Entry(customer).State != Microsoft.EntityFrameworkCore.EntityState.Added) result.Updated++;
                }
                seen.Add(reference);

                customer.SetContact(row.Get("contact_name") ?? customer.ContactName, row.Get("phone") ?? customer.Phone, Email(row.Get("email")) ?? customer.Email);
                customer.SetBilling(row.Get("billing_line1") ?? customer.BillingLine1, row.Get("billing_line2") ?? customer.BillingLine2,
                    row.Get("billing_city") ?? customer.BillingCity, row.Get("billing_country") ?? customer.BillingCountry);
                customer.SetNotes(row.Get("notes") ?? customer.Notes);

                if (row.Get("delivery_label") is { } label)
                {
                    var line1 = row.Get("delivery_line1") ?? throw new RowException("delivery_line1", "Enter the delivery address.");
                    var existing = customer.Addresses.FirstOrDefault(a => string.Equals(a.Label, label.Trim(), StringComparison.OrdinalIgnoreCase));
                    if (existing is null)
                    {
                        customer.AddAddress(label, line1, row.Get("delivery_line2"), row.Get("delivery_city"), row.Get("delivery_country"), row.Get("delivery_contact"), row.Get("delivery_phone"), false);
                    }
                    else
                    {
                        customer.UpdateAddress(existing.Id, label, line1, row.Get("delivery_line2") ?? existing.Line2, row.Get("delivery_city") ?? existing.City,
                            row.Get("delivery_country") ?? existing.Country, row.Get("delivery_contact") ?? existing.ContactName, row.Get("delivery_phone") ?? existing.Phone, existing.IsDefault);
                    }
                }

                if (row.Bool("active") is { } active)
                {
                    if (active) customer.Activate(); else customer.Deactivate();
                }
            });
        }
    }

    private static string? Email(string? value)
    {
        if (value is null) return null;
        if (!value.Contains('@', StringComparison.Ordinal) || value.Contains(' ', StringComparison.Ordinal)) throw new RowException("email", $"{value} is not a valid email address.");
        return value;
    }

    // ---- Locations --------------------------------------------------------------------------

    private async Task ImportLocationsAsync(List<Row> rows, List<ImportError> errors, Counter result, CancellationToken ct)
    {
        var warehouseCodes = rows.Select(r => r.Get("warehouse_code")?.ToUpperInvariant()).OfType<string>().Distinct().ToList();
        var warehouses = await db.Warehouses.AsNoTracking().Where(w => warehouseCodes.Contains(w.Code)).ToDictionaryAsync(w => w.Code, w => w.Id, ct);
        var allowed = new Dictionary<Guid, bool>();
        foreach (var id in warehouses.Values)
        {
            try { await access.EnsureCanManageAsync(id, ct); allowed[id] = true; }
            catch (NotFoundException) { allowed[id] = false; }
        }
        var ids = warehouses.Values.ToList();
        var locations = await db.WarehouseLocations.Where(l => ids.Contains(l.WarehouseId)).ToDictionaryAsync(l => (l.WarehouseId, l.Code), ct);
        var codes = rows.Select(r => r.Get("barcode")).OfType<string>().Distinct().ToList();
        var productBarcodes = await db.ProductBarcodes.AsNoTracking().Where(b => codes.Contains(b.Value)).Select(b => b.Value).ToListAsync(ct);
        var binBarcodes = await db.WarehouseLocations.AsNoTracking().Where(l => l.Barcode != null && codes.Contains(l.Barcode))
            .Select(l => new { l.Id, Barcode = l.Barcode! }).ToListAsync(ct);
        var fileBarcodes = new Dictionary<string, (Guid, string)>();

        foreach (var row in rows)
        {
            await RowAsync(row, errors, () =>
            {
                var whCode = row.Required("warehouse_code").ToUpperInvariant();
                if (!warehouses.TryGetValue(whCode, out var warehouseId) || !allowed[warehouseId])
                {
                    throw new RowException("warehouse_code", $"Unknown warehouse {whCode}, or you do not have access to it.");
                }
                var type = row.Get("type") is { } t
                    ? Enum.TryParse<LocationType>(t, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed) ? parsed
                        : throw new RowException("type", "Type must be storage, receiving, dispatch or quarantine.")
                    : (LocationType?)null;

                var code = row.Required("code").ToUpperInvariant();
                if (!locations.TryGetValue((warehouseId, code), out var location))
                {
                    location = WarehouseLocation.Create(warehouseId, code, type ?? LocationType.Storage, row.Get("zone"), null, row.Bool("pickable"));
                    db.WarehouseLocations.Add(location);
                    locations[(warehouseId, location.Code)] = location;
                    result.Created++;
                }
                else
                {
                    var newType = type ?? location.Type;
                    location.Update(row.Get("zone") ?? location.Zone, newType, location.Barcode, row.Bool("pickable") ?? (location.IsPickable && newType == LocationType.Storage));
                    result.Updated++;
                }

                if (row.Get("barcode") is { } raw)
                {
                    var barcode = Barcodes.Normalize(raw);
                    if (productBarcodes.Contains(barcode)) throw new RowException("barcode", $"Barcode {barcode} is already used by a product.");
                    if (binBarcodes.Any(b => b.Barcode == barcode && b.Id != location.Id)) throw new RowException("barcode", $"Barcode {barcode} is already used by another bin.");
                    if (fileBarcodes.TryGetValue(barcode, out var other) && other.Item1 != location.Id) throw new RowException("barcode", $"Barcode {barcode} appears more than once in the file ({other.Item2}).");
                    fileBarcodes[barcode] = (location.Id, location.Code);
                    location.Update(location.Zone, location.Type, barcode, location.IsPickable);
                }

                if (row.Bool("active") is { } active)
                {
                    if (active) location.Activate(); else location.Deactivate();
                }
            });
        }
    }

    // ---- CSV plumbing -----------------------------------------------------------------------

    private static async Task<List<Row>> ReadAsync(Stream content, ImportKind kind, List<ImportError> errors, CancellationToken ct)
    {
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            DetectDelimiter = true,
            DetectDelimiterValues = [",", ";", "\t"],
            TrimOptions = TrimOptions.Trim,
            BadDataFound = null,
            MissingFieldFound = null,
        };
        using var reader = new StreamReader(content, detectEncodingFromByteOrderMarks: true);
        using var csv = new CsvReader(reader, config);
        var rows = new List<Row>();
        if (!await csv.ReadAsync() || !csv.ReadHeader())
        {
            errors.Add(new ImportError(0, null, "The file is empty."));
            return rows;
        }
        var headers = csv.HeaderRecord!.Select(h => h.Trim().ToLowerInvariant().Replace(' ', '_')).ToArray();
        var required = kind switch { ImportKind.Products => RequiredProducts, ImportKind.Customers => RequiredCustomers, _ => RequiredLocations };
        var missing = required.Except(headers).ToList();
        if (missing.Count > 0)
        {
            errors.Add(new ImportError(1, null, $"Missing column(s): {string.Join(", ", missing)}. Download the template for the expected columns."));
            return rows;
        }

        var line = 1;
        while (await csv.ReadAsync())
        {
            ct.ThrowIfCancellationRequested();
            line++;
            var values = new Dictionary<string, string?>();
            for (var i = 0; i < headers.Length; i++)
            {
                var v = csv.TryGetField<string>(i, out var s) ? s : null;
                values[headers[i]] = string.IsNullOrWhiteSpace(v) ? null : v.Trim();
            }
            if (values.Values.All(v => v is null)) continue; // skip blank lines
            rows.Add(new Row(line, values));
            if (rows.Count > MaxRows)
            {
                errors.Add(new ImportError(line, null, $"Files are limited to {MaxRows} rows. Split the file."));
                break;
            }
        }
        if (rows.Count == 0 && errors.Count == 0) errors.Add(new ImportError(0, null, "The file has a header but no data rows."));
        return rows;
    }

    private static Task RowAsync(Row row, List<ImportError> errors, Action apply)
    {
        try
        {
            apply();
        }
        catch (RowException e)
        {
            errors.Add(new ImportError(row.Line, e.Column, e.Message));
        }
        catch (DomainRuleException e)
        {
            errors.Add(new ImportError(row.Line, null, e.Message));
        }
        return Task.CompletedTask;
    }

    private sealed class Counter
    {
        public int Created { get; set; }
        public int Updated { get; set; }
    }

    private sealed class RowException(string column, string message) : Exception(message)
    {
        public string Column { get; } = column;
    }

    private sealed record Row(int Line, Dictionary<string, string?> Values)
    {
        public string? Get(string column) => Values.GetValueOrDefault(column);

        public string Required(string column) => Get(column) ?? throw new RowException(column, $"{column} is required.");

        public bool? Bool(string column) => Get(column)?.ToLowerInvariant() switch
        {
            null => null,
            "yes" or "y" or "true" or "1" or "oui" => true,
            "no" or "n" or "false" or "0" or "non" => false,
            var v => throw new RowException(column, $"“{v}” is not yes/no."),
        };

        public decimal? Decimal(string column)
        {
            var v = Get(column);
            if (v is null) return null;
            if (!v.Contains('.', StringComparison.Ordinal)) v = v.Replace(',', '.'); // "2,5" from French-locale Excel
            return decimal.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : throw new RowException(column, $"“{Get(column)}” is not a number.");
        }

        public int? Int(string column)
        {
            var v = Get(column);
            if (v is null) return null;
            return int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : throw new RowException(column, $"“{v}” is not a whole number.");
        }
    }
}
