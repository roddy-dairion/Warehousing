using System.Net;
using Inventory.Application.Common;
using Inventory.Application.Inventory;
using static Inventory.IntegrationTests.Http;

namespace Inventory.IntegrationTests;

public sealed class InventoryQueryTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Summary_shows_sellable_stock_and_low_stock_flags()
    {
        var kit = await InventoryKit.CreateAsync(api);
        var low = await kit.ProductAsync(minStock: 20);
        var ok = await kit.ProductAsync(minStock: 5);
        var empty = await kit.ProductAsync(minStock: 1);
        var shelf = await kit.BinAsync();
        await kit.ReceiveAsync(InventoryKit.Line(low.Id, 8, shelf), InventoryKit.Line(low.Id, 4), InventoryKit.Line(ok.Id, 9, shelf));
        await kit.ReserveAsync(Guid.NewGuid(), [new ReservationRequest(Guid.NewGuid(), low.Id, 3)]);

        var page = (await kit.Admin.GetJsonAsync<PagedResult<StockSummaryDto>>($"/api/v1/inventory/summary?warehouseId={kit.WarehouseId}&pageSize=200"))!;
        var l = page.Items.Single(x => x.ProductId == low.Id);
        (l.OnHand, l.Reserved, l.Available, l.Sellable, l.IsLow).ShouldBe((12m, 3m, 9m, 5m, true));
        page.Items.Single(x => x.ProductId == ok.Id).IsLow.ShouldBeFalse();
        page.Items.Single(x => x.ProductId == empty.Id).IsLow.ShouldBeTrue();

        var lowOnly = (await kit.Admin.GetJsonAsync<PagedResult<StockSummaryDto>>($"/api/v1/inventory/summary?warehouseId={kit.WarehouseId}&lowStockOnly=true&pageSize=200"))!;
        lowOnly.Items.Select(x => x.ProductId).ShouldBe([low.Id, empty.Id], ignoreOrder: true);

        var sorted = (await kit.Admin.GetJsonAsync<PagedResult<StockSummaryDto>>($"/api/v1/inventory/summary?warehouseId={kit.WarehouseId}&onlyWithStock=true&sortBy=onHand&sortDir=desc"))!;
        sorted.Items[0].ProductId.ShouldBe(low.Id);
    }

    [Fact]
    public async Task Product_stock_lists_bins_and_lots_in_accessible_warehouses_only()
    {
        var kit = await InventoryKit.CreateAsync(api);
        var p = await kit.ProductAsync(lots: true);
        var other = await api.CreateWarehouseAsync(Unique("W")[..8]);
        await kit.ReceiveAsync(InventoryKit.Line(p.Id, 3, await kit.BinAsync(), "A1"), InventoryKit.Line(p.Id, 2, await kit.BinAsync(), "B2"));
        var draft = await (await kit.Admin.PostJsonAsync("/api/v1/receipts", new { warehouseId = other, lines = new[] { InventoryKit.Line(p.Id, 7, null, "C3") } })).ReadAsync<ReceiptDto>();
        await kit.Admin.PostAsync($"/api/v1/receipts/{draft.Id}/post");

        var all = (await kit.Admin.GetJsonAsync<ProductStockDto>($"/api/v1/inventory/products/{p.Id}"))!;
        all.OnHand.ShouldBe(12);
        all.Warehouses.Single(w => w.WarehouseId == kit.WarehouseId).Balances.Count.ShouldBe(2);

        var (op, _) = await api.SignInAsync(await api.CreateUserAsync(RoleCodes.Operator, kit.WarehouseId));
        var mine = (await op.GetJsonAsync<ProductStockDto>($"/api/v1/inventory/products/{p.Id}"))!;
        mine.OnHand.ShouldBe(5);
        mine.Warehouses.ShouldHaveSingleItem().WarehouseId.ShouldBe(kit.WarehouseId);
        (await op.GetAsync($"/api/v1/inventory/products/{p.Id}?warehouseId={other}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await op.GetAsync($"/api/v1/inventory/movements?warehouseId={other}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Expiring_lists_lots_within_the_window_with_their_bins()
    {
        var kit = await InventoryKit.CreateAsync(api);
        var p = await kit.ProductAsync(lots: true, expiry: true);
        var today = InventoryKit.Today;
        await kit.ReceiveAsync(
            InventoryKit.Line(p.Id, 1, await kit.BinAsync(), "GONE", today.AddDays(-2)),
            InventoryKit.Line(p.Id, 2, await kit.BinAsync(), "SOON", today.AddDays(10)),
            InventoryKit.Line(p.Id, 3, null, "SOON", today.AddDays(10)),
            InventoryKit.Line(p.Id, 4, await kit.BinAsync(), "LATER", today.AddDays(60)));

        var lots = (await kit.Admin.GetJsonAsync<List<ExpiringLotDto>>($"/api/v1/inventory/expiring?warehouseId={kit.WarehouseId}"))!.Where(l => l.ProductId == p.Id).ToList();
        lots.Select(l => l.LotNo).ShouldBe(["GONE", "SOON"]);
        lots[0].DaysLeft.ShouldBe(-2);
        lots[1].OnHand.ShouldBe(5);
        lots[1].Locations.Count.ShouldBe(2);

        var wide = (await kit.Admin.GetJsonAsync<List<ExpiringLotDto>>($"/api/v1/inventory/expiring?warehouseId={kit.WarehouseId}&days=90"))!.Where(l => l.ProductId == p.Id);
        wide.Count().ShouldBe(3);
    }

    [Fact]
    public async Task Movements_can_be_filtered_and_searched()
    {
        var kit = await InventoryKit.CreateAsync(api);
        var p = await kit.ProductAsync();
        var r = await kit.ReceiveAsync(InventoryKit.Line(p.Id, 3), InventoryKit.Line(p.Id, 4));

        var byNumber = (await kit.Admin.GetJsonAsync<PagedResult<MovementDto>>($"/api/v1/inventory/movements?warehouseId={kit.WarehouseId}&search={r.Number}"))!;
        byNumber.TotalCount.ShouldBe(2);
        byNumber.Items[0].OnHandAfter.ShouldBe(7); // newest first
        var none = (await kit.Admin.GetJsonAsync<PagedResult<MovementDto>>($"/api/v1/inventory/movements?warehouseId={kit.WarehouseId}&productId={p.Id}&type=adjustmentIn"))!;
        none.TotalCount.ShouldBe(0);
    }
}
