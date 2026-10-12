using System.Net.Http.Json;
using Inventory.Application.Common;
using Inventory.Application.Inventory;
using Inventory.Application.Products;
using Inventory.Application.Warehouses;
using Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Inventory.IntegrationTests.Http;

namespace Inventory.IntegrationTests;

/// <summary>Helpers for stock tests: products, bins, receipts and direct reservation calls.</summary>
internal sealed class InventoryKit(ApiFactory api, HttpClient admin, Guid warehouseId, Dictionary<string, Guid> units)
{
    public ApiFactory Api => api;
    public HttpClient Admin => admin;
    public Guid WarehouseId => warehouseId;
    public Dictionary<string, Guid> Units => units;

    public static async Task<InventoryKit> CreateAsync(ApiFactory api)
    {
        var (admin, _) = await api.SignInAsync(ApiFactory.AdminEmail);
        var units = (await admin.GetJsonAsync<List<UnitDto>>("/api/v1/units"))!.ToDictionary(u => u.Code, u => u.Id);
        return new InventoryKit(api, admin, await api.MainWarehouseIdAsync(), units);
    }

    public static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    public async Task<ProductDto> ProductAsync(bool lots = false, bool expiry = false, decimal? minStock = null, int? cartonOf = null, string unit = "EA")
    {
        var body = new
        {
            sku = Unique("SKU"),
            name = "Test product",
            baseUnitId = units[unit],
            minStockQty = minStock,
            isLotTracked = lots,
            hasExpiry = expiry,
            units = cartonOf is null ? Array.Empty<object>() : [new { unitId = units["CTN"], factorToBase = cartonOf.Value }],
        };
        return await (await admin.PostJsonAsync("/api/v1/products", body)).ReadAsync<ProductDto>();
    }

    public async Task<Guid> BinAsync(string? code = null, bool pickable = true, Guid? warehouse = null)
    {
        var body = new { code = code ?? Unique("A"), type = "storage", isPickable = pickable };
        var bin = await (await admin.PostJsonAsync($"/api/v1/warehouses/{warehouse ?? warehouseId}/locations", body)).ReadAsync<LocationDto>();
        return bin.Id;
    }

    public Task<Guid> SystemBinAsync(string code, Guid? warehouse = null) =>
        api.WithDbAsync((db, _) => db.WarehouseLocations.Where(l => l.WarehouseId == (warehouse ?? warehouseId) && l.Code == code).Select(l => l.Id).SingleAsync());

    public static object Line(Guid productId, decimal qty, Guid? bin = null, string? lot = null, DateOnly? expiry = null, Guid? productUnitId = null) =>
        new { productId, quantity = qty, locationId = bin, lotNo = lot, expiryDate = expiry, productUnitId };

    public async Task<ReceiptDto> DraftReceiptAsync(params object[] lines) =>
        await (await admin.PostJsonAsync("/api/v1/receipts", new { warehouseId, supplierName = "Supplier Ltd", lines })).ReadAsync<ReceiptDto>();

    /// <summary>Creates and posts a receipt (admin).</summary>
    public async Task<ReceiptDto> ReceiveAsync(params object[] lines)
    {
        var draft = await DraftReceiptAsync(lines);
        return await (await admin.PostAsync($"/api/v1/receipts/{draft.Id}/post")).ReadAsync<ReceiptDto>();
    }

    public async Task<List<BalanceDto>> BalancesAsync(Guid productId, Guid? warehouse = null) =>
        (await admin.GetJsonAsync<PagedResult<BalanceDto>>($"/api/v1/inventory/balances?warehouseId={warehouse ?? warehouseId}&productId={productId}&onlyWithStock=false&pageSize=200"))!.Items.ToList();

    public async Task<decimal> OnHandAsync(Guid productId) => (await BalancesAsync(productId)).Sum(b => b.OnHand);

    public Task<int> MovementCountAsync(Guid productId) => api.WithDbAsync((db, _) => db.InventoryMovements.CountAsync(m => m.ProductId == productId));

    /// <summary>Calls the reservation engine directly (dispatch approval arrives in Phase 5).</summary>
    public Task<IReadOnlyList<Domain.Inventory.StockReservation>> ReserveAsync(Guid sourceId, IReadOnlyList<ReservationRequest> lines, DateOnly? on = null) =>
        api.WithDbAsync(async (db, sp) =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(Ct);
            var result = await sp.GetRequiredService<ReservationService>().ReserveAsync("test", sourceId, warehouseId, lines, on ?? Today, Ct);
            await db.SaveChangesAsync(Ct);
            await tx.CommitAsync(Ct);
            return result;
        });

    public Task WithTransactionAsync(Func<AppDbContext, IServiceProvider, Task> action) =>
        api.WithDbAsync(async (db, sp) =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(Ct);
            await action(db, sp);
            await db.SaveChangesAsync(Ct);
            await tx.CommitAsync(Ct);
        });

    public static async Task<HttpResponseMessage> PostWithKeyAsync(HttpClient client, string url, object? body, string key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = body is null ? null : JsonContent.Create(body, options: Json.Options) };
        request.Headers.Add("Idempotency-Key", key);
        return await client.SendAsync(request, Ct);
    }
}
