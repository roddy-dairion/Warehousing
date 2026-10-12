using System.Net;
using System.Net.Http.Headers;
using Inventory.Application.Customers;
using Inventory.Application.Dispatches;
using Inventory.Application.Products;
using static Inventory.IntegrationTests.Http;

namespace Inventory.IntegrationTests;

/// <summary>Helpers for dispatch tests: customers, stocked products, users per role and the workflow steps.</summary>
internal sealed class DispatchKit
{
    /// <summary>A real 1×1 PNG (signature / photo stand-in).</summary>
    public static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=");

    private DispatchKit(InventoryKit stock, HttpClient op, HttpClient manager, HttpClient manager2)
    {
        Stock = stock;
        Operator = op;
        Manager = manager;
        Manager2 = manager2;
    }

    public InventoryKit Stock { get; }
    public ApiFactory Api => Stock.Api;
    public HttpClient Admin => Stock.Admin;
    public Guid WarehouseId => Stock.WarehouseId;
    /// <summary>Creates, submits, picks and delivers.</summary>
    public HttpClient Operator { get; }
    /// <summary>Approves and ships.</summary>
    public HttpClient Manager { get; }
    public HttpClient Manager2 { get; }

    public static async Task<DispatchKit> CreateAsync(ApiFactory api)
    {
        var stock = await InventoryKit.CreateAsync(api);
        var (op, _) = await api.SignInAsync(await api.CreateUserAsync(RoleCodes.Operator, stock.WarehouseId));
        var (m1, _) = await api.SignInAsync(await api.CreateUserAsync(RoleCodes.Manager, stock.WarehouseId));
        var (m2, _) = await api.SignInAsync(await api.CreateUserAsync(RoleCodes.Manager, stock.WarehouseId));
        return new DispatchKit(stock, op, m1, m2);
    }

    public async Task<CustomerDto> CustomerAsync(string? email = "orders@customer.test", bool withAddress = true)
    {
        var body = new
        {
            reference = Unique("C"),
            companyName = "Customer Ltd",
            contactName = "Jane Buyer",
            email,
            addresses = withAddress ? new[] { new { label = "Shop", line1 = "1 Main Street", city = "Port Louis", country = "Mauritius", isDefault = true } } : [],
        };
        return await (await Admin.PostJsonAsync("/api/v1/customers", body)).ReadAsync<CustomerDto>();
    }

    /// <summary>A product with a base barcode (and a carton barcode when <paramref name="cartonOf"/> is set), received into a pickable bin.</summary>
    public async Task<(ProductDto Product, Guid Bin, string Barcode)> StockedProductAsync(decimal quantity, bool lots = false, int? cartonOf = null, string? lot = null, Guid? bin = null)
    {
        var barcode = Unique("EAN");
        var units = Stock.Units;
        var body = new
        {
            sku = Unique("SKU"),
            name = "Dispatch product",
            baseUnitId = units["EA"],
            isLotTracked = lots,
            hasExpiry = false,
            units = cartonOf is null ? Array.Empty<object>() : [new { unitId = units["CTN"], factorToBase = cartonOf.Value }],
            barcodes = cartonOf is null
                ? new object[] { new { value = barcode } }
                : [new { value = barcode }, new { value = barcode + "-CTN", unitId = units["CTN"] }],
        };
        var product = await (await Admin.PostJsonAsync("/api/v1/products", body)).ReadAsync<ProductDto>();
        var binId = bin ?? await Stock.BinAsync();
        if (quantity > 0) await Stock.ReceiveAsync(InventoryKit.Line(product.Id, quantity, binId, lots ? lot ?? "LOT-1" : null));
        return (product, binId, barcode);
    }

    public static object Line(Guid productId, decimal quantity, Guid? productUnitId = null) => new { productId, quantity, productUnitId };

    public async Task<DispatchDto> DraftAsync(Guid customerId, params object[] lines) =>
        await (await Operator.PostJsonAsync("/api/v1/dispatches", new { warehouseId = WarehouseId, customerId, customerOrderReference = "PO-1", lines })).ReadAsync<DispatchDto>();

    public async Task<DispatchDto> SubmittedAsync(Guid customerId, params object[] lines)
    {
        var d = await DraftAsync(customerId, lines);
        return await (await Operator.PostAsync($"/api/v1/dispatches/{d.Id}/submit")).ReadAsync<DispatchDto>();
    }

    public async Task<DispatchDto> ApprovedAsync(Guid customerId, params object[] lines)
    {
        var d = await SubmittedAsync(customerId, lines);
        return await (await Manager.PostJsonAsync($"/api/v1/dispatches/{d.Id}/approve", new { })).ReadAsync<DispatchDto>();
    }

    public async Task<PickListDto> PickListAsync(Guid id) => (await Operator.GetJsonAsync<PickListDto>($"/api/v1/dispatches/{id}/pick-list"))!;

    public Task<HttpResponseMessage> PickAsync(Guid id, string location, Guid productId, decimal qty, string? lot = null, string? scanId = null) =>
        Operator.PostJsonAsync($"/api/v1/dispatches/{id}/picks", new { scanId = scanId ?? Guid.NewGuid().ToString(), location, productId, lotNo = lot, quantity = qty });

    public Task<HttpResponseMessage> ScanAsync(Guid id, string location, string barcode, decimal? qty = null, string? lot = null, string? scanId = null) =>
        Operator.PostJsonAsync($"/api/v1/dispatches/{id}/picks", new { scanId = scanId ?? Guid.NewGuid().ToString(), location, barcode, lotNo = lot, quantity = qty });

    /// <summary>Picks every task in full (or the given fraction per task).</summary>
    public async Task PickAllAsync(Guid id, decimal fraction = 1)
    {
        foreach (var t in (await PickListAsync(id)).Tasks.Where(t => t.Remaining > 0))
        {
            var qty = Math.Floor(t.Remaining * fraction);
            if (qty <= 0) continue;
            (await PickAsync(id, t.LocationCode, t.ProductId, qty, t.LotNo)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }
    }

    public async Task<DispatchDto> ReadyAsync(Guid id, bool allowShort = false) =>
        await (await Operator.PostJsonAsync($"/api/v1/dispatches/{id}/ready", new { allowShort })).ReadAsync<DispatchDto>();

    public async Task<DispatchDto> ShippedAsync(Guid customerId, params object[] lines)
    {
        var d = await ApprovedAsync(customerId, lines);
        await PickAllAsync(d.Id);
        await ReadyAsync(d.Id);
        return await (await Manager.PostJsonAsync($"/api/v1/dispatches/{d.Id}/ship", new { carrier = "Own van", trackingNumber = "VAN-1" })).ReadAsync<DispatchDto>();
    }

    public static MultipartFormDataContent DeliveryForm(string? receiver = "John Receiver", bool signature = true, int photos = 1, byte[]? photoBytes = null)
    {
        var form = new MultipartFormDataContent();
        if (receiver is not null) form.Add(new StringContent(receiver), "receiverName");
        if (signature) form.Add(Image(Png, "signature.png"), "signature", "signature.png");
        for (var i = 0; i < photos; i++) form.Add(Image(photoBytes ?? Png, $"photo{i}.jpg"), "photos", $"photo{i}.jpg");
        return form;
    }

    public Task<HttpResponseMessage> DeliverAsync(Guid id, MultipartFormDataContent form) => Operator.PostAsync($"/api/v1/dispatches/{id}/deliver", form, Ct);

    private static ByteArrayContent Image(byte[] bytes, string name)
    {
        var c = new ByteArrayContent(bytes);
        c.Headers.ContentType = new MediaTypeHeaderValue(name.EndsWith(".png", StringComparison.Ordinal) ? "image/png" : "image/jpeg");
        return c;
    }
}
