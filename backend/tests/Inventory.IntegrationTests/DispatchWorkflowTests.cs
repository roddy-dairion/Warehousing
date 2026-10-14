using System.Net;
using Inventory.Application.Common;
using Inventory.Application.Dispatches;
using Inventory.Application.Inventory;
using Inventory.Domain.Dispatches;
using Inventory.Domain.Inventory;
using static Inventory.IntegrationTests.Http;

namespace Inventory.IntegrationTests;

public sealed class DispatchWorkflowTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Full_flow_reserves_at_approval_and_deducts_once_at_shipment()
    {
        var kit = await DispatchKit.CreateAsync(api);
        var customer = await kit.CustomerAsync();
        var (p, bin, barcode) = await kit.StockedProductAsync(20);

        var draft = await kit.DraftAsync(customer.Id, DispatchKit.Line(p.Id, 8));
        draft.Number.ShouldMatch(@"^DSP-MAIN-\d{4}-\d{6}$");
        draft.Status.ShouldBe(DispatchStatus.Draft);
        draft.Actions.CanSubmit.ShouldBeTrue();
        (await kit.Stock.BalancesAsync(p.Id)).Single().Reserved.ShouldBe(0);

        var submitted = await (await kit.Operator.PostAsync($"/api/v1/dispatches/{draft.Id}/submit")).ReadAsync<DispatchDto>();
        submitted.ShipTo!.Line1.ShouldBe("1 Main Street");
        (await kit.Operator.PostJsonAsync($"/api/v1/dispatches/{draft.Id}/approve", new { })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var approved = await (await kit.Manager.PostJsonAsync($"/api/v1/dispatches/{draft.Id}/approve", new { note = "OK" })).ReadAsync<DispatchDto>();
        approved.Status.ShouldBe(DispatchStatus.Approved);
        approved.Lines.Single().QuantityReserved.ShouldBe(8);
        var b = (await kit.Stock.BalancesAsync(p.Id)).Single();
        (b.OnHand, b.Reserved, b.Available).ShouldBe((20m, 8m, 12m));

        var list = await kit.PickListAsync(draft.Id);
        var task = list.Tasks.ShouldHaveSingleItem();
        task.LocationId.ShouldBe(bin);
        (await kit.ScanAsync(draft.Id, task.LocationCode, barcode, 5)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var last = await (await kit.ScanAsync(draft.Id, task.LocationCode, barcode, 3)).ReadAsync<PickResultDto>();
        (last.LinePicked, last.TaskRemaining, last.Status).ShouldBe((8m, 0m, DispatchStatus.Picking));

        (await kit.ReadyAsync(draft.Id)).Status.ShouldBe(DispatchStatus.ReadyForDispatch);
        (await kit.Operator.PostJsonAsync($"/api/v1/dispatches/{draft.Id}/ship", new { })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var shipped = await (await kit.Manager.PostJsonAsync($"/api/v1/dispatches/{draft.Id}/ship", new { carrier = "DHL", trackingNumber = "TRK1" })).ReadAsync<DispatchDto>();
        shipped.Status.ShouldBe(DispatchStatus.Dispatched);
        shipped.Lines.Single().QuantityShipped.ShouldBe(8);
        shipped.Carrier.ShouldBe("DHL");
        shipped.ShippedByName.ShouldNotBeNull();
        b = (await kit.Stock.BalancesAsync(p.Id)).Single();
        (b.OnHand, b.Reserved, b.Available).ShouldBe((12m, 0m, 12m));

        var again = await (await kit.Manager.PostJsonAsync($"/api/v1/dispatches/{draft.Id}/ship", new { })).ProblemAsync(HttpStatusCode.Conflict);
        again.Code().ShouldBe("dispatch.invalid_status");
        (await kit.Stock.OnHandAsync(p.Id)).ShouldBe(12);

        var movement = (await kit.Admin.GetJsonAsync<PagedResult<MovementDto>>($"/api/v1/inventory/movements?warehouseId={kit.WarehouseId}&productId={p.Id}&type=dispatch"))!.Items.Single();
        (movement.Quantity, movement.SourceNumber).ShouldBe((-8m, draft.Number));

        shipped.History.Select(h => h.To).ShouldBe([DispatchStatus.Draft, DispatchStatus.Submitted, DispatchStatus.Approved, DispatchStatus.Picking,
            DispatchStatus.ReadyForDispatch, DispatchStatus.Dispatched]);
        shipped.Documents.ShouldContain(d => d.Type == Domain.Documents.DocumentType.PackingSlip);

        var check = await kit.Admin.GetJsonAsync<ReconciliationResult>($"/api/v1/inventory/reconciliation?warehouseId={kit.WarehouseId}");
        check!.Issues.ShouldBeEmpty();
    }

    [Fact]
    public async Task Creator_cannot_approve_and_shortage_reserves_nothing()
    {
        var kit = await DispatchKit.CreateAsync(api);
        var customer = await kit.CustomerAsync();
        var (p, _, _) = await kit.StockedProductAsync(5);
        var (q, _, _) = await kit.StockedProductAsync(50);

        var own = await (await kit.Manager.PostJsonAsync("/api/v1/dispatches", new { warehouseId = kit.WarehouseId, customerId = customer.Id, lines = new[] { DispatchKit.Line(q.Id, 1) } }))
            .ReadAsync<DispatchDto>();
        await kit.Manager.PostAsync($"/api/v1/dispatches/{own.Id}/submit");
        (await (await kit.Manager.PostJsonAsync($"/api/v1/dispatches/{own.Id}/approve", new { })).ProblemAsync(HttpStatusCode.Conflict)).Code().ShouldBe("dispatch.self_approval");
        (await kit.Manager2.PostJsonAsync($"/api/v1/dispatches/{own.Id}/approve", new { })).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Administrators may approve their own dispatches (permission dispatches.approve.own).
        var adminOwn = await (await kit.Admin.PostJsonAsync("/api/v1/dispatches", new { warehouseId = kit.WarehouseId, customerId = customer.Id, lines = new[] { DispatchKit.Line(q.Id, 1) } }))
            .ReadAsync<DispatchDto>();
        var submitted = await (await kit.Admin.PostAsync($"/api/v1/dispatches/{adminOwn.Id}/submit")).ReadAsync<DispatchDto>();
        submitted.Actions.CanApprove.ShouldBeTrue();
        (await (await kit.Admin.PostJsonAsync($"/api/v1/dispatches/{adminOwn.Id}/approve", new { })).ReadAsync<DispatchDto>()).Status.ShouldBe(DispatchStatus.Approved);

        var d = await kit.SubmittedAsync(customer.Id, DispatchKit.Line(q.Id, 10), DispatchKit.Line(p.Id, 6));
        var problem = await (await kit.Manager.PostJsonAsync($"/api/v1/dispatches/{d.Id}/approve", new { })).ProblemAsync(HttpStatusCode.Conflict);
        problem.Code().ShouldBe("stock.shortage");
        var shortage = problem.GetProperty("shortages").EnumerateArray().Single();
        shortage.GetProperty("sku").GetString().ShouldBe(p.Sku);
        shortage.GetProperty("available").GetDecimal().ShouldBe(5);

        (await kit.Manager.GetJsonAsync<DispatchDto>($"/api/v1/dispatches/{d.Id}"))!.Status.ShouldBe(DispatchStatus.Submitted);
        (await kit.Stock.BalancesAsync(q.Id)).Sum(x => x.Reserved).ShouldBe(2); // only the two approved dispatches above
    }

    [Fact]
    public async Task Submission_validates_lines_customer_and_address()
    {
        var kit = await DispatchKit.CreateAsync(api);
        var customer = await kit.CustomerAsync();
        var (p, _, _) = await kit.StockedProductAsync(5);

        var empty = await kit.DraftAsync(customer.Id);
        (await (await kit.Operator.PostAsync($"/api/v1/dispatches/{empty.Id}/submit")).ProblemAsync(HttpStatusCode.UnprocessableEntity)).Code().ShouldBe("dispatch.empty");

        var dup = await kit.Operator.PostJsonAsync("/api/v1/dispatches", new { warehouseId = kit.WarehouseId, customerId = customer.Id, lines = new[] { DispatchKit.Line(p.Id, 1), DispatchKit.Line(p.Id, 2) } });
        (await dup.ProblemAsync(HttpStatusCode.UnprocessableEntity)).Code().ShouldBe("dispatch.duplicate_product");

        var noAddress = await kit.CustomerAsync(withAddress: false);
        var d = await kit.DraftAsync(noAddress.Id, DispatchKit.Line(p.Id, 1));
        (await (await kit.Operator.PostAsync($"/api/v1/dispatches/{d.Id}/submit")).ProblemAsync(HttpStatusCode.UnprocessableEntity)).Code().ShouldBe("address.required");

        // Return to draft with a reason, then edit.
        var s = await kit.SubmittedAsync(customer.Id, DispatchKit.Line(p.Id, 1));
        (await kit.Manager.PostJsonAsync($"/api/v1/dispatches/{s.Id}/return", new { reason = "" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var back = await (await kit.Manager.PostJsonAsync($"/api/v1/dispatches/{s.Id}/return", new { reason = "Wrong quantity" })).ReadAsync<DispatchDto>();
        back.Status.ShouldBe(DispatchStatus.Draft);
        back.History[^1].Note.ShouldBe("Wrong quantity");
        var edited = await (await kit.Operator.PutJsonAsync($"/api/v1/dispatches/{s.Id}", new
        {
            customerId = customer.Id, requestedDate = InventoryKit.Today, lines = new[] { DispatchKit.Line(p.Id, 3) }, version = back.Version,
        })).ReadAsync<DispatchDto>();
        edited.Lines.Single().Quantity.ShouldBe(3);
        (await (await kit.Operator.PutJsonAsync($"/api/v1/dispatches/{s.Id}", new
        {
            customerId = customer.Id, requestedDate = InventoryKit.Today, lines = new[] { DispatchKit.Line(p.Id, 3) }, version = back.Version,
        })).ProblemAsync(HttpStatusCode.Conflict)).Code().ShouldBe("concurrency_conflict");
    }

    [Fact]
    public async Task Picking_checks_product_bin_quantity_and_ignores_repeated_scans()
    {
        var kit = await DispatchKit.CreateAsync(api);
        var customer = await kit.CustomerAsync();
        var (p, bin, barcode) = await kit.StockedProductAsync(30, cartonOf: 6);
        var (other, _, otherBarcode) = await kit.StockedProductAsync(5);
        var d = await kit.ApprovedAsync(customer.Id, DispatchKit.Line(p.Id, 2, p.Units.Single().Id)); // 2 cartons = 12
        d.Lines.Single().Quantity.ShouldBe(12);
        var binCode = (await kit.PickListAsync(d.Id)).Tasks.Single().LocationCode;

        (await (await kit.ScanAsync(d.Id, binCode, "NO-SUCH-CODE")).ProblemAsync(HttpStatusCode.NotFound)).Code().ShouldBe("not_found");
        (await (await kit.ScanAsync(d.Id, binCode, otherBarcode)).ProblemAsync(HttpStatusCode.UnprocessableEntity)).Code().ShouldBe("pick.not_on_dispatch");
        var wrongBin = await kit.Stock.BinAsync();
        var notAllocated = await (await kit.PickAsync(d.Id, (await GetBinCode(kit, wrongBin)), p.Id, 1)).ProblemAsync(HttpStatusCode.UnprocessableEntity);
        notAllocated.Code().ShouldBe("pick.not_allocated");
        notAllocated.GetProperty("detail").GetString()!.ShouldContain(binCode);

        // A carton barcode counts 6.
        var scanId = Guid.NewGuid().ToString();
        var carton = await (await kit.ScanAsync(d.Id, binCode, barcode + "-CTN", 1, scanId: scanId)).ReadAsync<PickResultDto>();
        carton.Quantity.ShouldBe(6);
        var replay = await (await kit.ScanAsync(d.Id, binCode, barcode + "-CTN", 1, scanId: scanId)).ReadAsync<PickResultDto>();
        replay.Replayed.ShouldBeTrue();
        replay.LinePicked.ShouldBe(6);

        (await (await kit.ScanAsync(d.Id, binCode, barcode, 7)).ProblemAsync(HttpStatusCode.UnprocessableEntity)).Code().ShouldBe("pick.over_pick");

        // Undo, then pick again.
        var undone = await (await kit.Operator.PostJsonAsync($"/api/v1/dispatches/{d.Id}/picks/{carton.PickId}/undo", new { scanId = Guid.NewGuid().ToString() })).ReadAsync<PickResultDto>();
        undone.LinePicked.ShouldBe(0);
        (await (await kit.Operator.PostJsonAsync($"/api/v1/dispatches/{d.Id}/picks/{carton.PickId}/undo", new { scanId = Guid.NewGuid().ToString() }))
            .ProblemAsync(HttpStatusCode.Conflict)).Code().ShouldBe("pick.already_undone");
        (await kit.ScanAsync(d.Id, binCode, barcode, 12)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var discrepancy = await (await kit.Operator.PostJsonAsync($"/api/v1/dispatches/{d.Id}/discrepancies", new { type = "damaged", lineId = d.Lines.Single().Id, quantity = 1, note = "Torn box" }))
            .ReadAsync<DiscrepancyDto>();
        discrepancy.Sku.ShouldBe(p.Sku);
        (await kit.Stock.OnHandAsync(p.Id)).ShouldBe(30); // picking does not move stock
        _ = other;
        _ = bin;
    }

    [Fact]
    public async Task Allocation_can_move_to_another_bin_before_picking()
    {
        var kit = await DispatchKit.CreateAsync(api);
        var customer = await kit.CustomerAsync();
        var (p, bin1, _) = await kit.StockedProductAsync(10);
        var bin2 = await kit.Stock.BinAsync();
        await kit.Stock.ReceiveAsync(InventoryKit.Line(p.Id, 10, bin2));
        var d = await kit.ApprovedAsync(customer.Id, DispatchKit.Line(p.Id, 6));
        var task = (await kit.PickListAsync(d.Id)).Tasks.Single();
        task.LocationId.ShouldBe(bin1);

        var bin2Code = await GetBinCode(kit, bin2);
        var moved = await (await kit.Operator.PostJsonAsync($"/api/v1/dispatches/{d.Id}/reallocate", new { reservationId = task.ReservationId, location = bin2Code, quantity = 4 }))
            .ReadAsync<PickListDto>();
        moved.Tasks.Select(t => (t.LocationCode, t.Quantity)).OrderBy(x => x.Quantity).ShouldBe([(task.LocationCode, 2m), (bin2Code, 4m)]);
        var balances = await kit.Stock.BalancesAsync(p.Id);
        balances.Single(x => x.LocationId == bin1).Reserved.ShouldBe(2);
        balances.Single(x => x.LocationId == bin2).Reserved.ShouldBe(4);

        await kit.PickAllAsync(d.Id);
        await kit.ReadyAsync(d.Id);
        await (await kit.Manager.PostJsonAsync($"/api/v1/dispatches/{d.Id}/ship", new { })).ReadAsync<DispatchDto>();
        balances = await kit.Stock.BalancesAsync(p.Id);
        (balances.Single(x => x.LocationId == bin1).OnHand, balances.Single(x => x.LocationId == bin2).OnHand).ShouldBe((8m, 6m));
        balances.Sum(x => x.Reserved).ShouldBe(0);
    }

    [Fact]
    public async Task Short_shipment_can_close_short()
    {
        var kit = await DispatchKit.CreateAsync(api);
        var customer = await kit.CustomerAsync();
        var (p, _, _) = await kit.StockedProductAsync(10);
        var d = await kit.ApprovedAsync(customer.Id, DispatchKit.Line(p.Id, 10));
        await kit.PickAllAsync(d.Id, 0.7m); // 7

        (await (await kit.Operator.PostJsonAsync($"/api/v1/dispatches/{d.Id}/ready", new { allowShort = false })).ProblemAsync(HttpStatusCode.Conflict))
            .Code().ShouldBe("dispatch.picking_incomplete");
        await kit.ReadyAsync(d.Id, allowShort: true);
        (await (await kit.Manager.PostJsonAsync($"/api/v1/dispatches/{d.Id}/ship", new { })).ProblemAsync(HttpStatusCode.UnprocessableEntity))
            .Code().ShouldBe("dispatch.short_decision_required");

        var shipped = await (await kit.Manager.PostJsonAsync($"/api/v1/dispatches/{d.Id}/ship", new { shortDecision = "closeShort" })).ReadAsync<DispatchDto>();
        shipped.ShortDecision.ShouldBe(ShortShipDecision.CloseShort);
        shipped.Lines.Single().QuantityShipped.ShouldBe(7);
        shipped.Backorders.ShouldBeEmpty();
        var b = (await kit.Stock.BalancesAsync(p.Id)).Single();
        (b.OnHand, b.Reserved).ShouldBe((3m, 0m));
    }

    [Fact]
    public async Task Short_shipment_can_create_a_backorder_that_keeps_the_reservation()
    {
        var kit = await DispatchKit.CreateAsync(api);
        var customer = await kit.CustomerAsync();
        var (p, _, _) = await kit.StockedProductAsync(10);
        var (q, _, _) = await kit.StockedProductAsync(10);
        var d = await kit.ApprovedAsync(customer.Id, DispatchKit.Line(p.Id, 10), DispatchKit.Line(q.Id, 4));
        var tasks = (await kit.PickListAsync(d.Id)).Tasks;
        (await kit.PickAsync(d.Id, tasks.Single(t => t.ProductId == p.Id).LocationCode, p.Id, 6)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await kit.PickAsync(d.Id, tasks.Single(t => t.ProductId == q.Id).LocationCode, q.Id, 4)).StatusCode.ShouldBe(HttpStatusCode.OK);
        await kit.ReadyAsync(d.Id, allowShort: true);

        var shipped = await (await kit.Manager.PostJsonAsync($"/api/v1/dispatches/{d.Id}/ship", new { shortDecision = "backorder" })).ReadAsync<DispatchDto>();
        var childRef = shipped.Backorders.ShouldHaveSingleItem();
        childRef.Status.ShouldBe(DispatchStatus.Approved);
        var pb = (await kit.Stock.BalancesAsync(p.Id)).Single();
        (pb.OnHand, pb.Reserved).ShouldBe((4m, 4m)); // the rest stays reserved for the backorder

        var child = (await kit.Manager.GetJsonAsync<DispatchDto>($"/api/v1/dispatches/{childRef.Id}"))!;
        child.BackorderOfNumber.ShouldBe(d.Number);
        var line = child.Lines.ShouldHaveSingleItem();
        (line.ProductId, line.Quantity, line.QuantityReserved).ShouldBe((p.Id, 4m, 4m));

        await kit.PickAllAsync(child.Id);
        await kit.ReadyAsync(child.Id);
        await (await kit.Manager.PostJsonAsync($"/api/v1/dispatches/{child.Id}/ship", new { })).ReadAsync<DispatchDto>();
        pb = (await kit.Stock.BalancesAsync(p.Id)).Single();
        (pb.OnHand, pb.Reserved).ShouldBe((0m, 0m));
        (await kit.Admin.GetJsonAsync<ReconciliationResult>($"/api/v1/inventory/reconciliation?warehouseId={kit.WarehouseId}"))!.Issues.ShouldBeEmpty();
    }

    [Fact]
    public async Task Cancel_releases_reservations_but_shipped_dispatches_cannot_be_cancelled()
    {
        var kit = await DispatchKit.CreateAsync(api);
        var customer = await kit.CustomerAsync();
        var (p, _, _) = await kit.StockedProductAsync(10);
        var d = await kit.ApprovedAsync(customer.Id, DispatchKit.Line(p.Id, 5));
        await kit.PickAllAsync(d.Id, 0.4m);

        (await kit.Operator.PostJsonAsync($"/api/v1/dispatches/{d.Id}/cancel", new { reason = "Customer cancelled" })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await kit.Manager.PostJsonAsync($"/api/v1/dispatches/{d.Id}/cancel", new { reason = " " })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var cancelled = await (await kit.Manager.PostJsonAsync($"/api/v1/dispatches/{d.Id}/cancel", new { reason = "Customer cancelled" })).ReadAsync<DispatchDto>();
        cancelled.Status.ShouldBe(DispatchStatus.Cancelled);
        cancelled.CancelReason.ShouldBe("Customer cancelled");
        (await kit.Stock.BalancesAsync(p.Id)).Single().Reserved.ShouldBe(0);

        var shipped = await kit.ShippedAsync(customer.Id, DispatchKit.Line(p.Id, 2));
        (await (await kit.Manager.PostJsonAsync($"/api/v1/dispatches/{shipped.Id}/cancel", new { reason = "Too late" })).ProblemAsync(HttpStatusCode.Conflict))
            .Code().ShouldBe("dispatch.cannot_cancel_shipped");
        (await kit.Stock.OnHandAsync(p.Id)).ShouldBe(8);
    }

    [Fact]
    public async Task Dispatches_are_scoped_to_the_users_warehouses_and_picker_must_qualify()
    {
        var kit = await DispatchKit.CreateAsync(api);
        var customer = await kit.CustomerAsync();
        var (p, _, _) = await kit.StockedProductAsync(10);
        var other = await api.CreateWarehouseAsync(Unique("W")[..8]);
        var foreign = await (await kit.Admin.PostJsonAsync("/api/v1/dispatches", new { warehouseId = other, customerId = customer.Id, lines = new[] { DispatchKit.Line(p.Id, 1) } }))
            .ReadAsync<DispatchDto>();
        (await kit.Operator.GetAsync($"/api/v1/dispatches/{foreign.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await kit.Operator.GetAsync($"/api/v1/dispatches?warehouseId={other}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await kit.Operator.PostAsync($"/api/v1/dispatches/{foreign.Id}/submit")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var d = await kit.ApprovedAsync(customer.Id, DispatchKit.Line(p.Id, 1));
        var outsiderEmail = await api.CreateUserAsync(RoleCodes.Operator, other);
        var outsider = await api.WithDbAsync((db, _) => Task.FromResult(db.Users.Single(u => u.Email == outsiderEmail).Id));
        (await (await kit.Manager.PostJsonAsync($"/api/v1/dispatches/{d.Id}/picking", new { assignedToId = outsider })).ProblemAsync(HttpStatusCode.UnprocessableEntity))
            .Code().ShouldBe("dispatch.assignee_invalid");

        var (_, me) = await api.SignInAsync(await api.CreateUserAsync(RoleCodes.Operator, kit.WarehouseId));
        var started = await (await kit.Manager.PostJsonAsync($"/api/v1/dispatches/{d.Id}/picking", new { assignedToId = me.User.Id })).ReadAsync<DispatchDto>();
        started.AssignedToName.ShouldBe($"Test {RoleCodes.Operator}");

        var mine = (await kit.Manager.GetJsonAsync<PagedResult<DispatchListItemDto>>($"/api/v1/dispatches?warehouseId={kit.WarehouseId}&assignedToId={me.User.Id}"))!;
        mine.Items.ShouldHaveSingleItem().Id.ShouldBe(d.Id);
        var open = (await kit.Manager.GetJsonAsync<PagedResult<DispatchListItemDto>>($"/api/v1/dispatches?warehouseId={kit.WarehouseId}&open=true&search={customer.Reference}"))!;
        open.Items.ShouldContain(x => x.Id == d.Id);
    }

    private static Task<string> GetBinCode(DispatchKit kit, Guid binId) =>
        kit.Api.WithDbAsync((db, _) => Task.FromResult(db.WarehouseLocations.Single(l => l.Id == binId).Code));
}
