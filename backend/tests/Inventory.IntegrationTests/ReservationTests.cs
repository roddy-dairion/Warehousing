using System.Net;
using Inventory.Application.Common;
using Inventory.Application.Inventory;
using Inventory.Domain.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Inventory.IntegrationTests.Http;

namespace Inventory.IntegrationTests;

public sealed class ReservationTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Allocation_is_FEFO_and_skips_unusable_stock()
    {
        var kit = await InventoryKit.CreateAsync(api);
        var p = await kit.ProductAsync(lots: true, expiry: true);
        var (shelf1, shelf2, overflow) = (await kit.BinAsync(), await kit.BinAsync(), await kit.BinAsync(pickable: false));
        var today = InventoryKit.Today;

        await kit.ReceiveAsync(
            InventoryKit.Line(p.Id, 10, shelf1, "LATE", today.AddDays(100)),
            InventoryKit.Line(p.Id, 10, shelf2, "SOON", today.AddDays(30)),
            InventoryKit.Line(p.Id, 10, shelf1, "EXPIRED", today.AddDays(-1)),
            InventoryKit.Line(p.Id, 10, shelf2, "BLOCKED", today.AddDays(10)),
            InventoryKit.Line(p.Id, 10, overflow, "SOONEST", today.AddDays(5)),
            InventoryKit.Line(p.Id, 10, null, "RECV", today.AddDays(2)));

        var blocked = (await kit.Admin.GetJsonAsync<List<LotDto>>($"/api/v1/products/{p.Id}/lots"))!.Single(l => l.LotNo == "BLOCKED");
        (await kit.Admin.PostJsonAsync($"/api/v1/lots/{blocked.Id}/status", new { status = "blocked", version = blocked.Version })).StatusCode
            .ShouldBe(HttpStatusCode.BadRequest); // reason required
        (await (await kit.Admin.PostJsonAsync($"/api/v1/lots/{blocked.Id}/status", new { status = "blocked", reason = "Recall", version = blocked.Version }))
            .ReadAsync<LotDto>()).Status.ShouldBe(LotStatus.Blocked);

        var reservations = await kit.ReserveAsync(Guid.NewGuid(), [new ReservationRequest(Guid.NewGuid(), p.Id, 15)]);
        var lots = await api.WithDbAsync((db, _) => db.ProductLots.Where(l => l.ProductId == p.Id).ToDictionaryAsync(l => l.Id, l => l.LotNo, Ct));
        var balances = await kit.BalancesAsync(p.Id);
        var byLot = reservations.ToDictionary(r => balances.Single(b => b.Id == r.BalanceId).LotNo!, r => r.QuantityReserved);
        byLot.ShouldBe(new Dictionary<string, decimal> { ["SOON"] = 10, ["LATE"] = 5 });
        lots.Count.ShouldBe(6);

        var shortage = await Should.ThrowAsync<StockShortageException>(() => kit.ReserveAsync(Guid.NewGuid(), [new ReservationRequest(Guid.NewGuid(), p.Id, 6)]));
        shortage.Code.ShouldBe("stock.shortage");
        shortage.Shortages.Single().Available.ShouldBe(5);

        // A minimum remaining shelf life is applied against the dispatch date.
        await Should.ThrowAsync<StockShortageException>(() => kit.ReserveAsync(Guid.NewGuid(), [new ReservationRequest(Guid.NewGuid(), p.Id, 1)], today.AddDays(101)));
    }

    [Fact]
    public async Task Concurrent_reservations_never_over_allocate()
    {
        var kit = await InventoryKit.CreateAsync(api);
        var p = await kit.ProductAsync();
        await kit.ReceiveAsync(InventoryKit.Line(p.Id, 6, await kit.BinAsync()), InventoryKit.Line(p.Id, 4, await kit.BinAsync()));

        var attempts = await Task.WhenAll(Enumerable.Range(0, 10).Select(async _ =>
        {
            try
            {
                await kit.ReserveAsync(Guid.NewGuid(), [new ReservationRequest(Guid.NewGuid(), p.Id, 3)]);
                return true;
            }
            catch (StockShortageException)
            {
                return false;
            }
        }));

        attempts.Count(ok => ok).ShouldBe(3);
        var balances = await kit.BalancesAsync(p.Id);
        balances.Sum(b => b.Reserved).ShouldBe(9);
        balances.ShouldAllBe(b => b.Reserved <= b.OnHand);
        var check = await api.WithDbAsync((_, sp) => sp.GetRequiredService<ReconciliationService>().CheckAsync(kit.WarehouseId, Ct));
        check.Issues.Where(i => i.Sku == p.Sku).ShouldBeEmpty();
    }

    [Fact]
    public async Task Reserving_twice_for_the_same_document_is_a_no_op()
    {
        var kit = await InventoryKit.CreateAsync(api);
        var p = await kit.ProductAsync();
        await kit.ReceiveAsync(InventoryKit.Line(p.Id, 10, await kit.BinAsync()));
        var doc = Guid.NewGuid();
        var line = new ReservationRequest(Guid.NewGuid(), p.Id, 4);

        var first = await kit.ReserveAsync(doc, [line]);
        var second = await kit.ReserveAsync(doc, [line]);
        second.Select(r => r.Id).ShouldBe(first.Select(r => r.Id));
        (await kit.BalancesAsync(p.Id)).Sum(b => b.Reserved).ShouldBe(4);
    }

    [Fact]
    public async Task Consume_ships_against_the_reservation_and_release_frees_the_rest()
    {
        var kit = await InventoryKit.CreateAsync(api);
        var p = await kit.ProductAsync();
        await kit.ReceiveAsync(InventoryKit.Line(p.Id, 10, await kit.BinAsync()));
        var doc = Guid.NewGuid();
        var reservation = (await kit.ReserveAsync(doc, [new ReservationRequest(Guid.NewGuid(), p.Id, 8)])).Single();

        await kit.WithTransactionAsync((_, sp) => sp.GetRequiredService<ReservationService>()
            .ConsumeAsync("test", doc, "DSP-TEST-1", [new ReservationConsumption(reservation.Id, 5)], Ct));
        var b = (await kit.BalancesAsync(p.Id)).Single();
        (b.OnHand, b.Reserved, b.Available).ShouldBe((5m, 3m, 2m));

        // More than is still reserved: rejected, nothing changes.
        var over = await Should.ThrowAsync<Domain.Common.DomainRuleException>(() => kit.WithTransactionAsync((_, sp) => sp.GetRequiredService<ReservationService>()
            .ConsumeAsync("test", doc, "DSP-TEST-1", [new ReservationConsumption(reservation.Id, 4)], Ct)));
        over.Code.ShouldBe("reservation.consume");

        decimal released = 0;
        await kit.WithTransactionAsync(async (_, sp) => released = await sp.GetRequiredService<ReservationService>().ReleaseAsync("test", doc, null, Ct));
        released.ShouldBe(3);
        b = (await kit.BalancesAsync(p.Id)).Single();
        (b.OnHand, b.Reserved, b.Available).ShouldBe((5m, 0m, 5m));

        var saved = await api.WithDbAsync((db, _) => db.StockReservations.SingleAsync(r => r.Id == reservation.Id, Ct));
        (saved.Status, saved.QuantityConsumed, saved.QuantityReleased).ShouldBe((ReservationStatus.Consumed, 5m, 3m));
        var movement = (await kit.Admin.GetJsonAsync<PagedResult<MovementDto>>($"/api/v1/inventory/movements?warehouseId={kit.WarehouseId}&productId={p.Id}&type=dispatch"))!.Items.Single();
        (movement.Quantity, movement.SourceNumber).ShouldBe((-5m, "DSP-TEST-1"));
    }

    [Fact]
    public async Task Reconciliation_reports_balances_that_disagree_with_the_ledger()
    {
        var kit = await InventoryKit.CreateAsync(api);
        var p = await kit.ProductAsync();
        await kit.ReceiveAsync(InventoryKit.Line(p.Id, 10, await kit.BinAsync()));
        var url = $"/api/v1/inventory/reconciliation?warehouseId={kit.WarehouseId}";
        (await kit.Admin.GetJsonAsync<ReconciliationResult>(url))!.Issues.ShouldBeEmpty();

        await api.WithDbAsync((db, _) => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE inventory_balances SET on_hand = 11 WHERE product_id = {p.Id}", Ct));
        var issue = (await kit.Admin.GetJsonAsync<ReconciliationResult>(url))!.Issues.ShouldHaveSingleItem();
        (issue.Sku, issue.OnHand, issue.LedgerOnHand).ShouldBe((p.Sku, 11m, 10m));

        var (op, _) = await api.SignInAsync(await api.CreateUserAsync(RoleCodes.Operator, kit.WarehouseId));
        (await op.GetAsync(url, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
