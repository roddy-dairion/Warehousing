using System.Net;
using System.Text.Json;
using Inventory.Application.Dispatches;
using Inventory.Application.Inventory;
using static Inventory.IntegrationTests.Http;

namespace Inventory.IntegrationTests;

/// <summary>
/// Phase 8: a user assigned to one branch cannot read or change anything that belongs to another branch,
/// through any warehouse-scoped endpoint (documents, stock queries, reports, dashboard, bins).
/// </summary>
public sealed class WarehouseIsolationTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task A_branch_user_gets_404_or_403_for_every_resource_of_another_branch()
    {
        var kit = await DispatchKit.CreateAsync(api);
        var stock = kit.Stock;
        var other = await api.CreateWarehouseAsync(Unique("W")[..8]);
        var customer = await kit.CustomerAsync();
        var product = await stock.ProductAsync();

        // Data in the other branch, created by an administrator (all branches).
        var binB = await stock.BinAsync(warehouse: other);
        var receiptB = await (await kit.Admin.PostJsonAsync("/api/v1/receipts", new { warehouseId = other, lines = new[] { InventoryKit.Line(product.Id, 10, binB) } }))
            .ReadAsync<ReceiptDto>();
        await kit.Admin.PostAsync($"/api/v1/receipts/{receiptB.Id}/post");
        var draftB = await (await kit.Admin.PostJsonAsync("/api/v1/receipts", new { warehouseId = other, lines = new[] { InventoryKit.Line(product.Id, 1, binB) } }))
            .ReadAsync<ReceiptDto>();
        var adjB = await (await kit.Admin.PostJsonAsync("/api/v1/adjustments", new
        {
            warehouseId = other, reason = "found", lines = new[] { new { productId = product.Id, locationId = binB, quantityDelta = 1m } },
        })).ReadAsync<AdjustmentDto>();
        var receivingB = await stock.SystemBinAsync("RECEIVING", other);
        var moveB = await (await kit.Admin.PostJsonAsync("/api/v1/transfers/bin-moves", new
        {
            warehouseId = other, lines = new[] { new { productId = product.Id, fromLocationId = binB, toLocationId = receivingB, quantity = 1m } },
        })).ReadAsync<TransferDto>();
        var dispatchB = await (await kit.Admin.PostJsonAsync("/api/v1/dispatches", new { warehouseId = other, customerId = customer.Id, lines = new[] { DispatchKit.Line(product.Id, 2) } }))
            .ReadAsync<DispatchDto>();

        // A manager of MAIN only (has nearly every permission, so only the branch scope can refuse).
        var manager = kit.Manager;
        var q = $"warehouseId={other}";
        var reads = new[]
        {
            $"/api/v1/receipts/{receiptB.Id}", $"/api/v1/receipts?{q}",
            $"/api/v1/adjustments/{adjB.Id}", $"/api/v1/adjustments?{q}",
            $"/api/v1/transfers/{moveB.Id}", $"/api/v1/transfers?{q}",
            $"/api/v1/dispatches/{dispatchB.Id}", $"/api/v1/dispatches?{q}", $"/api/v1/dispatches/{dispatchB.Id}/pick-list",
            $"/api/v1/dispatches/{dispatchB.Id}/documents/{Guid.NewGuid()}", $"/api/v1/dispatches/{dispatchB.Id}/pod/files/{Guid.NewGuid()}",
            $"/api/v1/inventory/balances?{q}", $"/api/v1/inventory/summary?{q}", $"/api/v1/inventory/movements?{q}",
            $"/api/v1/inventory/expiring?{q}", $"/api/v1/inventory/reconciliation?{q}",
            $"/api/v1/reports/stock-on-hand?{q}", $"/api/v1/reports/movements/csv?{q}", $"/api/v1/dashboard/warehouse?{q}",
            $"/api/v1/warehouses/{other}", $"/api/v1/warehouses/{other}/locations", $"/api/v1/locations/{binB}",
        };
        var leaks = new List<string>();
        foreach (var url in reads)
        {
            var status = (await manager.GetAsync(url, Ct)).StatusCode;
            if (status is not (HttpStatusCode.NotFound or HttpStatusCode.Forbidden)) leaks.Add($"GET {url} -> {(int)status}");
        }

        var writes = new (string Url, object? Body)[]
        {
            ("/api/v1/receipts", new { warehouseId = other, lines = new[] { InventoryKit.Line(product.Id, 1) } }),
            ($"/api/v1/receipts/{draftB.Id}/post", null), ($"/api/v1/receipts/{draftB.Id}/cancel", null),
            ($"/api/v1/receipts/{receiptB.Id}/reverse", new { reason = "test" }),
            ("/api/v1/adjustments", new { warehouseId = other, reason = "found", lines = new[] { new { productId = product.Id, locationId = binB, quantityDelta = 1m } } }),
            ($"/api/v1/adjustments/{adjB.Id}/submit", null), ($"/api/v1/adjustments/{adjB.Id}/approve", new { note = (string?)null }),
            ("/api/v1/transfers/bin-moves", new { warehouseId = other, lines = new[] { new { productId = product.Id, fromLocationId = binB, toLocationId = receivingB, quantity = 1m } } }),
            ("/api/v1/dispatches", new { warehouseId = other, customerId = customer.Id, lines = new[] { DispatchKit.Line(product.Id, 1) } }),
            ($"/api/v1/dispatches/{dispatchB.Id}/submit", null), ($"/api/v1/dispatches/{dispatchB.Id}/cancel", new { reason = "test" }),
            ($"/api/v1/dispatches/{dispatchB.Id}/picks", new { scanId = Guid.NewGuid().ToString(), location = "x", productId = product.Id, quantity = 1 }),
            ($"/api/v1/warehouses/{other}/locations", new { code = "ZZ-1", type = "storage" }),
        };
        foreach (var (url, body) in writes)
        {
            var response = await manager.PostJsonAsync(url, body ?? new { });
            if (response.StatusCode is not (HttpStatusCode.NotFound or HttpStatusCode.Forbidden)) leaks.Add($"POST {url} -> {(int)response.StatusCode}");
        }
        leaks.ShouldBeEmpty();

        // Product stock across branches only lists the branches the user may see.
        var productStock = await manager.GetJsonAsync<JsonElement>($"/api/v1/inventory/products/{product.Id}");
        productStock.GetProperty("warehouses").EnumerateArray().Select(w => w.GetProperty("warehouseId").GetGuid()).ShouldNotContain(other);
        (await manager.GetAsync($"/api/v1/inventory/products/{product.Id}?{q}", Ct)).StatusCode.ShouldBeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Forbidden);

        // Nothing changed in the other branch.
        (await kit.Admin.GetJsonAsync<ReceiptDto>($"/api/v1/receipts/{draftB.Id}"))!.Status.ShouldBe(Domain.Inventory.ReceiptStatus.Draft);
        (await kit.Admin.GetJsonAsync<DispatchDto>($"/api/v1/dispatches/{dispatchB.Id}"))!.Status.ShouldBe(Domain.Dispatches.DispatchStatus.Draft);
        (await stock.BalancesAsync(product.Id, other)).Sum(b => b.OnHand).ShouldBe(10);
    }
}
