using System.Net;
using Inventory.Application.Common;
using Inventory.Application.Inventory;
using Inventory.Domain.Inventory;
using Microsoft.EntityFrameworkCore;
using static Inventory.IntegrationTests.Http;

namespace Inventory.IntegrationTests;

public sealed class ReceiptTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Draft_has_no_stock_effect_and_posting_adds_stock_once()
    {
        var kit = await InventoryKit.CreateAsync(api);
        var p = await kit.ProductAsync();
        var bin = await kit.BinAsync();

        var draft = await kit.DraftReceiptAsync(InventoryKit.Line(p.Id, 40, bin));
        draft.Status.ShouldBe(ReceiptStatus.Draft);
        draft.Number.ShouldMatch(@"^GR-MAIN-\d{4}-\d{6}$");
        (await kit.OnHandAsync(p.Id)).ShouldBe(0);

        var posted = await (await kit.Admin.PostAsync($"/api/v1/receipts/{draft.Id}/post")).ReadAsync<ReceiptDto>();
        posted.Status.ShouldBe(ReceiptStatus.Posted);
        posted.PostedByName.ShouldNotBeNull();
        (await kit.OnHandAsync(p.Id)).ShouldBe(40);

        var again = await (await kit.Admin.PostAsync($"/api/v1/receipts/{draft.Id}/post")).ProblemAsync(HttpStatusCode.Conflict);
        again.Code().ShouldBe("receipt.already_posted");
        (await kit.OnHandAsync(p.Id)).ShouldBe(40);
        (await kit.MovementCountAsync(p.Id)).ShouldBe(1);

        var movements = (await kit.Admin.GetJsonAsync<PagedResult<MovementDto>>($"/api/v1/inventory/movements?warehouseId={kit.WarehouseId}&productId={p.Id}"))!;
        var m = movements.Items.ShouldHaveSingleItem();
        m.Type.ShouldBe(MovementType.Receipt);
        m.Quantity.ShouldBe(40);
        m.OnHandAfter.ShouldBe(40);
        m.SourceNumber.ShouldBe(draft.Number);
        m.CreatedByName.ShouldNotBeNull();
    }

    [Fact]
    public async Task Concurrent_posts_of_the_same_receipt_add_stock_exactly_once()
    {
        var kit = await InventoryKit.CreateAsync(api);
        var p = await kit.ProductAsync();
        var draft = await kit.DraftReceiptAsync(InventoryKit.Line(p.Id, 10), InventoryKit.Line(p.Id, 5));

        var clients = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ => (await api.SignInAsync(ApiFactory.AdminEmail)).Client));
        var responses = await Task.WhenAll(clients.Select(c => c.PostAsync($"/api/v1/receipts/{draft.Id}/post", null, Ct)));

        responses.Count(r => r.StatusCode == HttpStatusCode.OK).ShouldBe(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.Conflict).ShouldBe(7);
        (await kit.OnHandAsync(p.Id)).ShouldBe(15);
        (await kit.MovementCountAsync(p.Id)).ShouldBe(2);
    }

    [Fact]
    public async Task Lines_without_a_bin_go_to_receiving_and_pack_units_convert_to_base()
    {
        var kit = await InventoryKit.CreateAsync(api);
        var p = await kit.ProductAsync(cartonOf: 12);
        var carton = p.Units.Single().Id;

        var r = await kit.ReceiveAsync(InventoryKit.Line(p.Id, 2, productUnitId: carton));
        var line = r.Lines.Single();
        line.LocationCode.ShouldBe("RECEIVING");
        line.UnitCode.ShouldBe("CTN");
        line.EnteredQuantity.ShouldBe(2);
        line.Quantity.ShouldBe(24);
        (await kit.OnHandAsync(p.Id)).ShouldBe(24);

        var fraction = await (await kit.Admin.PostJsonAsync("/api/v1/receipts", new { warehouseId = kit.WarehouseId, lines = new[] { InventoryKit.Line(p.Id, 1.5m) } }))
            .ProblemAsync(HttpStatusCode.UnprocessableEntity);
        fraction.Code().ShouldBe("quantity.precision");

        var dock = await kit.SystemBinAsync("DOCK");
        (await (await kit.Admin.PostJsonAsync("/api/v1/receipts", new { warehouseId = kit.WarehouseId, lines = new[] { InventoryKit.Line(p.Id, 1, dock) } }))
            .ProblemAsync(HttpStatusCode.UnprocessableEntity)).Code().ShouldBe("receipt.dispatch_bin");
    }

    [Fact]
    public async Task Lot_tracked_products_need_a_lot_and_a_lot_keeps_one_expiry()
    {
        var kit = await InventoryKit.CreateAsync(api);
        var p = await kit.ProductAsync(lots: true, expiry: true);
        var bin = await kit.BinAsync();

        (await (await kit.Admin.PostJsonAsync("/api/v1/receipts", new { warehouseId = kit.WarehouseId, lines = new[] { InventoryKit.Line(p.Id, 5, bin) } }))
            .ProblemAsync(HttpStatusCode.UnprocessableEntity)).Code().ShouldBe("lot.required");

        // A new lot needs its expiry date when posting.
        var noExpiry = await kit.DraftReceiptAsync(InventoryKit.Line(p.Id, 5, bin, "l-001"));
        (await (await kit.Admin.PostAsync($"/api/v1/receipts/{noExpiry.Id}/post")).ProblemAsync(HttpStatusCode.UnprocessableEntity)).Code().ShouldBe("lot.expiry_required");
        (await kit.OnHandAsync(p.Id)).ShouldBe(0);

        var expiry = InventoryKit.Today.AddDays(200);
        await kit.ReceiveAsync(InventoryKit.Line(p.Id, 5, bin, "l-001", expiry));
        // Same lot again: the expiry may be omitted, but must match when given.
        await kit.ReceiveAsync(InventoryKit.Line(p.Id, 3, bin, "L-001"));
        var mismatch = await kit.DraftReceiptAsync(InventoryKit.Line(p.Id, 1, bin, "L-001", expiry.AddDays(1)));
        (await (await kit.Admin.PostAsync($"/api/v1/receipts/{mismatch.Id}/post")).ProblemAsync(HttpStatusCode.UnprocessableEntity)).Code().ShouldBe("lot.expiry_mismatch");

        var balance = (await kit.BalancesAsync(p.Id)).ShouldHaveSingleItem();
        balance.LotNo.ShouldBe("L-001");
        balance.ExpiryDate.ShouldBe(expiry);
        balance.OnHand.ShouldBe(8);

        var lots = (await kit.Admin.GetJsonAsync<List<LotDto>>($"/api/v1/products/{p.Id}/lots"))!;
        lots.ShouldHaveSingleItem().OnHand.ShouldBe(8);
    }

    [Fact]
    public async Task Reversal_removes_the_stock_but_not_when_it_is_reserved()
    {
        var kit = await InventoryKit.CreateAsync(api);
        var p = await kit.ProductAsync();
        var bin = await kit.BinAsync();
        var first = await kit.ReceiveAsync(InventoryKit.Line(p.Id, 10, bin));

        (await (await kit.Admin.PostJsonAsync($"/api/v1/receipts/{first.Id}/reverse", new { reason = "" })).ProblemAsync(HttpStatusCode.BadRequest))
            .Code().ShouldBe("validation_failed");

        var reversed = await (await kit.Admin.PostJsonAsync($"/api/v1/receipts/{first.Id}/reverse", new { reason = "Wrong delivery" })).ReadAsync<ReceiptDto>();
        reversed.Status.ShouldBe(ReceiptStatus.Reversed);
        reversed.ReversalReason.ShouldBe("Wrong delivery");
        (await kit.OnHandAsync(p.Id)).ShouldBe(0);
        (await (await kit.Admin.PostJsonAsync($"/api/v1/receipts/{first.Id}/reverse", new { reason = "again" })).ProblemAsync(HttpStatusCode.Conflict))
            .Code().ShouldBe("receipt.not_posted");

        var second = await kit.ReceiveAsync(InventoryKit.Line(p.Id, 10, bin));
        await kit.ReserveAsync(Guid.NewGuid(), [new ReservationRequest(Guid.NewGuid(), p.Id, 4)]);
        var blocked = await (await kit.Admin.PostJsonAsync($"/api/v1/receipts/{second.Id}/reverse", new { reason = "Mistake" })).ProblemAsync(HttpStatusCode.Conflict);
        blocked.Code().ShouldBe("stock.reserved_exceeds_on_hand");
        blocked.GetProperty("detail").GetString()!.ShouldContain(p.Sku);
        (await kit.OnHandAsync(p.Id)).ShouldBe(10);
        (await kit.Admin.GetJsonAsync<ReceiptDto>($"/api/v1/receipts/{second.Id}"))!.Status.ShouldBe(ReceiptStatus.Posted);
    }

    [Fact]
    public async Task Draft_edits_use_the_version_and_posted_receipts_cannot_change()
    {
        var kit = await InventoryKit.CreateAsync(api);
        var p = await kit.ProductAsync();
        var draft = await kit.DraftReceiptAsync(InventoryKit.Line(p.Id, 1));

        var update = new { receiptDate = InventoryKit.Today, supplierName = "New supplier", lines = new[] { InventoryKit.Line(p.Id, 7), InventoryKit.Line(p.Id, 3) }, version = draft.Version };
        var updated = await (await kit.Admin.PutJsonAsync($"/api/v1/receipts/{draft.Id}", update)).ReadAsync<ReceiptDto>();
        updated.Lines.Count.ShouldBe(2);
        updated.SupplierName.ShouldBe("New supplier");

        (await (await kit.Admin.PutJsonAsync($"/api/v1/receipts/{draft.Id}", update)).ProblemAsync(HttpStatusCode.Conflict)).Code().ShouldBe("concurrency_conflict");

        await kit.Admin.PostAsync($"/api/v1/receipts/{draft.Id}/post");
        (await (await kit.Admin.PutJsonAsync($"/api/v1/receipts/{draft.Id}", update with { version = updated.Version })).ProblemAsync(HttpStatusCode.Conflict))
            .Code().ShouldBeOneOf("receipt.not_draft", "concurrency_conflict");
        (await (await kit.Admin.PostAsync($"/api/v1/receipts/{draft.Id}/cancel")).ProblemAsync(HttpStatusCode.Conflict)).Code().ShouldBe("receipt.not_draft");

        var list = (await kit.Admin.GetJsonAsync<PagedResult<ReceiptListItemDto>>($"/api/v1/receipts?warehouseId={kit.WarehouseId}&status=posted"))!;
        list.Items.ShouldContain(r => r.Id == draft.Id && r.LineCount == 2);
    }

    [Fact]
    public async Task Operators_record_receipts_but_only_managers_post_them()
    {
        var kit = await InventoryKit.CreateAsync(api);
        var p = await kit.ProductAsync();
        var (op, _) = await api.SignInAsync(await api.CreateUserAsync(RoleCodes.Operator, kit.WarehouseId));
        var (manager, _) = await api.SignInAsync(await api.CreateUserAsync(RoleCodes.Manager, kit.WarehouseId));

        var draft = await (await op.PostJsonAsync("/api/v1/receipts", new { warehouseId = kit.WarehouseId, lines = new[] { InventoryKit.Line(p.Id, 2) } })).ReadAsync<ReceiptDto>();
        (await op.PostAsync($"/api/v1/receipts/{draft.Id}/post")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await manager.PostAsync($"/api/v1/receipts/{draft.Id}/post")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await kit.OnHandAsync(p.Id)).ShouldBe(2);
    }

    [Fact]
    public async Task Receipts_are_scoped_to_the_users_warehouses()
    {
        var kit = await InventoryKit.CreateAsync(api);
        var p = await kit.ProductAsync();
        var other = await api.CreateWarehouseAsync(Unique("WH")[..10]);
        var (op, _) = await api.SignInAsync(await api.CreateUserAsync(RoleCodes.Operator, kit.WarehouseId));

        var foreign = await (await kit.Admin.PostJsonAsync("/api/v1/receipts", new { warehouseId = other, lines = new[] { InventoryKit.Line(p.Id, 1) } })).ReadAsync<ReceiptDto>();
        (await op.GetAsync($"/api/v1/receipts/{foreign.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await op.GetAsync($"/api/v1/receipts?warehouseId={other}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await op.PostJsonAsync("/api/v1/receipts", new { warehouseId = other, lines = new[] { InventoryKit.Line(p.Id, 1) } })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await op.GetAsync($"/api/v1/inventory/balances?warehouseId={other}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // A bin of another warehouse cannot be used.
        var otherBin = await kit.SystemBinAsync("RECEIVING", other);
        (await (await kit.Admin.PostJsonAsync("/api/v1/receipts", new { warehouseId = kit.WarehouseId, lines = new[] { InventoryKit.Line(p.Id, 1, otherBin) } }))
            .ProblemAsync(HttpStatusCode.UnprocessableEntity)).Code().ShouldBe("location.other_warehouse");
    }

    [Fact]
    public async Task Idempotency_key_replays_the_first_response()
    {
        var kit = await InventoryKit.CreateAsync(api);
        var p = await kit.ProductAsync();
        var draft = await kit.DraftReceiptAsync(InventoryKit.Line(p.Id, 6));
        var key = Guid.NewGuid().ToString();

        var first = await InventoryKit.PostWithKeyAsync(kit.Admin, $"/api/v1/receipts/{draft.Id}/post", null, key);
        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        var firstBody = await first.Content.ReadAsStringAsync(Ct);

        var replay = await InventoryKit.PostWithKeyAsync(kit.Admin, $"/api/v1/receipts/{draft.Id}/post", null, key);
        replay.StatusCode.ShouldBe(HttpStatusCode.OK);
        replay.Headers.GetValues("Idempotent-Replayed").ShouldContain("true");
        (await replay.Content.ReadAsStringAsync(Ct)).ShouldBe(firstBody);
        (await kit.OnHandAsync(p.Id)).ShouldBe(6);

        // Same key for a different request → 422.
        var otherDraft = await kit.DraftReceiptAsync(InventoryKit.Line(p.Id, 1));
        (await (await InventoryKit.PostWithKeyAsync(kit.Admin, $"/api/v1/receipts/{otherDraft.Id}/post", null, key)).ProblemAsync(HttpStatusCode.UnprocessableEntity))
            .Code().ShouldBe("idempotency.key_reused");

        // A failed request is not stored: the same key can be retried.
        var failKey = Guid.NewGuid().ToString();
        var reverse = $"/api/v1/receipts/{otherDraft.Id}/reverse";
        (await InventoryKit.PostWithKeyAsync(kit.Admin, reverse, new { reason = "x" }, failKey)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await InventoryKit.PostWithKeyAsync(kit.Admin, reverse, new { reason = "x" }, failKey)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await api.WithDbAsync((db, _) => db.IdempotencyRecords.CountAsync(r => r.Key == failKey))).ShouldBe(0);

        // Keys are per user: another user with the same key runs their own request.
        var (manager, _) = await api.SignInAsync(await api.CreateUserAsync(RoleCodes.Manager, kit.WarehouseId));
        var mine = await InventoryKit.PostWithKeyAsync(manager, $"/api/v1/receipts/{otherDraft.Id}/post", null, key);
        mine.StatusCode.ShouldBe(HttpStatusCode.OK);
        mine.Headers.Contains("Idempotent-Replayed").ShouldBeFalse();
        (await kit.OnHandAsync(p.Id)).ShouldBe(7);
    }
}
