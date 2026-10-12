using System.Net;
using Inventory.Application.Common;
using Inventory.Application.Inventory;
using Inventory.Domain.Inventory;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using static Inventory.IntegrationTests.Http;

namespace Inventory.IntegrationTests;

public sealed class AdjustmentAndMoveTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private static object AdjLine(Guid productId, Guid bin, decimal delta, string? lot = null, DateOnly? expiry = null) =>
        new { productId, locationId = bin, quantityDelta = delta, lotNo = lot, expiryDate = expiry };

    [Fact]
    public async Task Operator_requests_and_a_manager_approves_the_adjustment()
    {
        var kit = await InventoryKit.CreateAsync(api);
        var p = await kit.ProductAsync();
        var bin = await kit.BinAsync();
        await kit.ReceiveAsync(InventoryKit.Line(p.Id, 10, bin));
        var (op, _) = await api.SignInAsync(await api.CreateUserAsync(RoleCodes.Operator, kit.WarehouseId));
        var (manager, _) = await api.SignInAsync(await api.CreateUserAsync(RoleCodes.Manager, kit.WarehouseId));

        var adj = await (await op.PostJsonAsync("/api/v1/adjustments", new { warehouseId = kit.WarehouseId, reason = "damage", lines = new[] { AdjLine(p.Id, bin, -3) } }))
            .ReadAsync<AdjustmentDto>();
        adj.Number.ShouldStartWith("ADJ-MAIN-");
        adj.Lines.Single().CurrentOnHand.ShouldBe(10);
        (await kit.OnHandAsync(p.Id)).ShouldBe(10);

        var submitted = await (await op.PostAsync($"/api/v1/adjustments/{adj.Id}/submit")).ReadAsync<AdjustmentDto>();
        submitted.Status.ShouldBe(AdjustmentStatus.PendingApproval);
        submitted.CanDecide.ShouldBeFalse();
        (await op.PostJsonAsync($"/api/v1/adjustments/{adj.Id}/approve", new { })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await manager.GetJsonAsync<AdjustmentDto>($"/api/v1/adjustments/{adj.Id}"))!.CanDecide.ShouldBeTrue();
        var posted = await (await manager.PostJsonAsync($"/api/v1/adjustments/{adj.Id}/approve", new { note = "Checked" })).ReadAsync<AdjustmentDto>();
        posted.Status.ShouldBe(AdjustmentStatus.Posted);
        posted.DecidedByName.ShouldBe($"Test {RoleCodes.Manager}");
        (await kit.OnHandAsync(p.Id)).ShouldBe(7);

        (await (await op.PostAsync($"/api/v1/adjustments/{adj.Id}/cancel")).ProblemAsync(HttpStatusCode.Conflict)).Code().ShouldBe("adjustment.cannot_cancel");
    }

    [Fact]
    public async Task Approver_cannot_approve_their_own_submission()
    {
        var kit = await InventoryKit.CreateAsync(api);
        var p = await kit.ProductAsync();
        var bin = await kit.BinAsync();
        var adj = await (await kit.Admin.PostJsonAsync("/api/v1/adjustments", new { warehouseId = kit.WarehouseId, reason = "found", lines = new[] { AdjLine(p.Id, bin, 4) } }))
            .ReadAsync<AdjustmentDto>();
        await kit.Admin.PostAsync($"/api/v1/adjustments/{adj.Id}/submit");

        (await (await kit.Admin.PostJsonAsync($"/api/v1/adjustments/{adj.Id}/approve", new { })).ProblemAsync(HttpStatusCode.Conflict)).Code().ShouldBe("adjustment.self_approval");

        var (manager, _) = await api.SignInAsync(await api.CreateUserAsync(RoleCodes.Manager, kit.WarehouseId));
        var rejected = await (await manager.PostJsonAsync($"/api/v1/adjustments/{adj.Id}/reject", new { reason = "Count again" })).ReadAsync<AdjustmentDto>();
        rejected.Status.ShouldBe(AdjustmentStatus.Rejected);
        rejected.DecisionNote.ShouldBe("Count again");
        var reopened = await (await kit.Admin.PostAsync($"/api/v1/adjustments/{adj.Id}/reopen")).ReadAsync<AdjustmentDto>();
        reopened.Status.ShouldBe(AdjustmentStatus.Draft);
        (await kit.OnHandAsync(p.Id)).ShouldBe(0);
    }

    [Fact]
    public async Task Approval_that_would_go_negative_fails_and_changes_nothing()
    {
        var kit = await InventoryKit.CreateAsync(api);
        var p = await kit.ProductAsync();
        var bin = await kit.BinAsync();
        await kit.ReceiveAsync(InventoryKit.Line(p.Id, 2, bin));
        var (manager, _) = await api.SignInAsync(await api.CreateUserAsync(RoleCodes.Manager, kit.WarehouseId));

        var adj = await (await kit.Admin.PostJsonAsync("/api/v1/adjustments", new
        {
            warehouseId = kit.WarehouseId, reason = "countCorrection",
            lines = new[] { AdjLine(p.Id, bin, 1), AdjLine(p.Id, bin, -5) },
        })).ReadAsync<AdjustmentDto>();
        await kit.Admin.PostAsync($"/api/v1/adjustments/{adj.Id}/submit");

        var problem = await (await manager.PostJsonAsync($"/api/v1/adjustments/{adj.Id}/approve", new { })).ProblemAsync(HttpStatusCode.Conflict);
        problem.Code().ShouldBe("stock.insufficient");
        problem.GetProperty("detail").GetString()!.ShouldContain(p.Sku);

        (await kit.OnHandAsync(p.Id)).ShouldBe(2);
        (await kit.MovementCountAsync(p.Id)).ShouldBe(1);
        (await manager.GetJsonAsync<AdjustmentDto>($"/api/v1/adjustments/{adj.Id}"))!.Status.ShouldBe(AdjustmentStatus.PendingApproval);
    }

    [Fact]
    public async Task Adjustments_can_add_new_lots_and_cannot_remove_reserved_stock()
    {
        var kit = await InventoryKit.CreateAsync(api);
        var p = await kit.ProductAsync(lots: true, expiry: true);
        var bin = await kit.BinAsync();
        var (manager, _) = await api.SignInAsync(await api.CreateUserAsync(RoleCodes.Manager, kit.WarehouseId));
        var expiry = InventoryKit.Today.AddDays(90);

        async Task<HttpResponseMessage> ApproveNew(object line)
        {
            var a = await (await kit.Admin.PostJsonAsync("/api/v1/adjustments", new { warehouseId = kit.WarehouseId, reason = "found", lines = new[] { line } })).ReadAsync<AdjustmentDto>();
            await kit.Admin.PostAsync($"/api/v1/adjustments/{a.Id}/submit");
            return await manager.PostJsonAsync($"/api/v1/adjustments/{a.Id}/approve", new { });
        }

        (await ApproveNew(AdjLine(p.Id, bin, 6, "NEW-1", expiry))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await kit.BalancesAsync(p.Id)).ShouldHaveSingleItem().LotNo.ShouldBe("NEW-1");
        (await (await ApproveNew(AdjLine(p.Id, bin, -1, "NOPE"))).ProblemAsync(HttpStatusCode.UnprocessableEntity)).Code().ShouldBe("lot.unknown");

        await kit.ReserveAsync(Guid.NewGuid(), [new ReservationRequest(Guid.NewGuid(), p.Id, 5)]);
        (await (await ApproveNew(AdjLine(p.Id, bin, -2, "NEW-1"))).ProblemAsync(HttpStatusCode.Conflict)).Code().ShouldBe("stock.reserved_exceeds_on_hand");
        (await ApproveNew(AdjLine(p.Id, bin, -1, "NEW-1"))).StatusCode.ShouldBe(HttpStatusCode.OK);
        var b = (await kit.BalancesAsync(p.Id)).Single();
        (b.OnHand, b.Reserved, b.Available).ShouldBe((5m, 5m, 0m));
    }

    [Fact]
    public async Task Bin_move_moves_unreserved_stock_only()
    {
        var kit = await InventoryKit.CreateAsync(api);
        var p = await kit.ProductAsync();
        var shelf = await kit.BinAsync();
        await kit.ReceiveAsync(InventoryKit.Line(p.Id, 12)); // RECEIVING
        var receiving = await kit.SystemBinAsync("RECEIVING");

        var move = await (await kit.Admin.PostJsonAsync("/api/v1/transfers/bin-moves", new
        {
            warehouseId = kit.WarehouseId, notes = "Put-away",
            lines = new[] { new { productId = p.Id, fromLocationId = receiving, toLocationId = shelf, quantity = 12m } },
        })).ReadAsync<TransferDto>();
        move.Number.ShouldStartWith("MOV-MAIN-");
        move.Lines.Single().ToLocationCode.ShouldNotBe("RECEIVING");

        var balances = await kit.BalancesAsync(p.Id);
        balances.Single(x => x.LocationId == receiving).OnHand.ShouldBe(0);
        balances.Single(x => x.LocationId == shelf).OnHand.ShouldBe(12);
        (await kit.OnHandAsync(p.Id)).ShouldBe(12);

        // Stock in RECEIVING could not be reserved; on the pickable shelf it can.
        await kit.ReserveAsync(Guid.NewGuid(), [new ReservationRequest(Guid.NewGuid(), p.Id, 10)]);
        var tooMuch = await kit.Admin.PostJsonAsync("/api/v1/transfers/bin-moves", new
        {
            warehouseId = kit.WarehouseId,
            lines = new[] { new { productId = p.Id, fromLocationId = shelf, toLocationId = receiving, quantity = 3m } },
        });
        (await tooMuch.ProblemAsync(HttpStatusCode.Conflict)).Code().ShouldBe("stock.reserved_exceeds_on_hand");

        var (op, _) = await api.SignInAsync(await api.CreateUserAsync(RoleCodes.Operator, kit.WarehouseId));
        (await op.PostJsonAsync("/api/v1/transfers/bin-moves", new
        {
            warehouseId = kit.WarehouseId,
            lines = new[] { new { productId = p.Id, fromLocationId = shelf, toLocationId = receiving, quantity = 1m } },
        })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var types = (await kit.Admin.GetJsonAsync<PagedResult<MovementDto>>($"/api/v1/inventory/movements?warehouseId={kit.WarehouseId}&sourceId={move.Id}"))!.Items.Select(m => m.Type).Order().ToList();
        types.ShouldBe([MovementType.MoveOut, MovementType.MoveIn]);
    }

    [Fact]
    public async Task Bin_move_with_idempotency_key_runs_once()
    {
        var kit = await InventoryKit.CreateAsync(api);
        var p = await kit.ProductAsync();
        var (a, b) = (await kit.BinAsync(), await kit.BinAsync());
        await kit.ReceiveAsync(InventoryKit.Line(p.Id, 5, a));
        var body = new { warehouseId = kit.WarehouseId, lines = new[] { new { productId = p.Id, fromLocationId = a, toLocationId = b, quantity = 2m } } };
        var key = Guid.NewGuid().ToString();

        var first = await InventoryKit.PostWithKeyAsync(kit.Admin, "/api/v1/transfers/bin-moves", body, key);
        var second = await InventoryKit.PostWithKeyAsync(kit.Admin, "/api/v1/transfers/bin-moves", body, key);
        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        second.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await second.ReadAsync<TransferDto>()).Id.ShouldBe((await first.ReadAsync<TransferDto>()).Id);
        (await kit.BalancesAsync(p.Id)).Single(x => x.LocationId == b).OnHand.ShouldBe(2);
    }

    [Fact]
    public async Task Master_data_with_stock_is_protected()
    {
        var kit = await InventoryKit.CreateAsync(api);
        var p = await kit.ProductAsync();
        var bin = await kit.BinAsync();
        await kit.ReceiveAsync(InventoryKit.Line(p.Id, 1, bin));

        (await (await kit.Admin.PostAsync($"/api/v1/locations/{bin}/deactivate")).ProblemAsync(HttpStatusCode.Conflict)).Code().ShouldBe("location.has_stock");

        var product = (await kit.Admin.GetJsonAsync<Application.Products.ProductDto>($"/api/v1/products/{p.Id}"))!;
        var change = new
        {
            name = product.Name, baseUnitId = product.BaseUnitId, isLotTracked = true, hasExpiry = false, version = product.Version,
        };
        (await (await kit.Admin.PutJsonAsync($"/api/v1/products/{p.Id}", change)).ProblemAsync(HttpStatusCode.Conflict)).Code().ShouldBe("product.has_stock_history");

        var other = await api.CreateWarehouseAsync(Unique("W")[..8]);
        var otherBin = await kit.BinAsync(warehouse: other);
        await kit.Admin.PostJsonAsync("/api/v1/receipts", new { warehouseId = other, lines = new[] { InventoryKit.Line(p.Id, 1, otherBin) } })
            .ContinueWith(async t => await kit.Admin.PostAsync($"/api/v1/receipts/{(await (await t).ReadAsync<ReceiptDto>()).Id}/post")).Unwrap();
        (await (await kit.Admin.PostAsync($"/api/v1/warehouses/{other}/deactivate")).ProblemAsync(HttpStatusCode.Conflict)).Code().ShouldBe("warehouse.has_stock");
    }

    [Fact]
    public async Task Ledger_rows_cannot_be_updated_or_deleted()
    {
        var kit = await InventoryKit.CreateAsync(api);
        var p = await kit.ProductAsync();
        await kit.ReceiveAsync(InventoryKit.Line(p.Id, 3));

        var update = await Should.ThrowAsync<PostgresException>(() => api.WithDbAsync((db, _) =>
            db.Database.ExecuteSqlInterpolatedAsync($"UPDATE inventory_movements SET quantity = 999 WHERE product_id = {p.Id}", Ct)));
        update.MessageText.ShouldContain("append-only");
        await Should.ThrowAsync<PostgresException>(() => api.WithDbAsync((db, _) =>
            db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM inventory_movements WHERE product_id = {p.Id}", Ct)));

        // The CHECK constraints back up the service rules.
        await Should.ThrowAsync<PostgresException>(() => api.WithDbAsync((db, _) =>
            db.Database.ExecuteSqlInterpolatedAsync($"UPDATE inventory_balances SET reserved = on_hand + 1 WHERE product_id = {p.Id}", Ct)));
    }
}
