using System.Collections.Concurrent;
using System.Net;
using Inventory.Application.Dispatches;
using Inventory.Application.Inventory;
using static Inventory.IntegrationTests.Http;

namespace Inventory.IntegrationTests;

/// <summary>
/// Phase 8 soak test: many stock-changing requests at the same time (approvals competing for the same stock,
/// adjustments, bin moves, receipts, picking, shipments, cancellations, repeated requests). Afterwards the stock
/// accounting must still hold: balances equal the ledger, reservations equal the reserved quantities, nothing is
/// negative or over-reserved, and the totals match what the successful requests did.
/// </summary>
public sealed class StockSoakTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Parallel_mixed_operations_keep_every_stock_invariant()
    {
        var kit = await DispatchKit.CreateAsync(api);
        var stock = kit.Stock;
        var customer = await kit.CustomerAsync();
        var rnd = new Random(42);

        // Three products, 40 each in a shelf bin, plus a second bin.
        var products = new List<(Guid Id, Guid BinA, Guid BinB)>();
        for (var i = 0; i < 3; i++)
        {
            var (p, binA, _) = await kit.StockedProductAsync(40);
            products.Add((p.Id, binA, await stock.BinAsync()));
        }
        decimal Initial() => 40m * products.Count;

        // Demand (12 dispatches × 12–20 of one product, about 64 per product) exceeds supply on purpose.
        var submitted = new List<DispatchDto>();
        for (var i = 0; i < 12; i++)
        {
            var p = products[i % products.Count];
            submitted.Add(await kit.SubmittedAsync(customer.Id, DispatchKit.Line(p.Id, rnd.Next(12, 21))));
        }
        // Adjustments of −2 submitted by an operator, approved later by the manager.
        var (op, _) = await api.SignInAsync(await api.CreateUserAsync(RoleCodes.Operator, kit.WarehouseId));
        var adjustments = new List<AdjustmentDto>();
        foreach (var p in products)
        {
            var adj = await (await op.PostJsonAsync("/api/v1/adjustments", new
            {
                warehouseId = kit.WarehouseId, reason = "damage", lines = new[] { new { productId = p.Id, locationId = p.BinA, quantityDelta = -2m } },
            })).ReadAsync<AdjustmentDto>();
            await op.PostAsync($"/api/v1/adjustments/{adj.Id}/submit");
            adjustments.Add(adj);
        }
        var receipts = new List<ReceiptDto>();
        foreach (var p in products) receipts.Add(await stock.DraftReceiptAsync(InventoryKit.Line(p.Id, 10, p.BinA)));

        // ---- Round 1, all at once: approvals, adjustment approvals, receipt posts (each twice), bin moves.
        var adjusted = new ConcurrentBag<decimal>();
        var received = new ConcurrentBag<decimal>();
        var round1 = new List<Task>();
        round1.AddRange(submitted.Select(d => kit.Manager.PostJsonAsync($"/api/v1/dispatches/{d.Id}/approve", new { })));
        round1.AddRange(adjustments.Select(async a =>
        {
            var r = await kit.Manager.PostJsonAsync($"/api/v1/adjustments/{a.Id}/approve", new { });
            if (r.StatusCode == HttpStatusCode.OK) adjusted.Add(-2);
        }));
        // Each receipt is posted twice at the same moment with one idempotency key (a retried request).
        round1.AddRange(receipts.SelectMany(r => new[] { r, r }).Select(r =>
            InventoryKit.PostWithKeyAsync(stock.Admin, $"/api/v1/receipts/{r.Id}/post", null, $"post-{r.Id}")));
        round1.AddRange(products.Select(p => stock.Admin.PostJsonAsync("/api/v1/transfers/bin-moves", new
        {
            warehouseId = kit.WarehouseId, lines = new[] { new { productId = p.Id, fromLocationId = p.BinA, toLocationId = p.BinB, quantity = 5m } },
        })));
        await Task.WhenAll(round1);
        foreach (var r in receipts)
        {
            var now = (await stock.Admin.GetJsonAsync<ReceiptDto>($"/api/v1/receipts/{r.Id}"))!;
            if (now.Status == Domain.Inventory.ReceiptStatus.Posted) received.Add(now.Lines.Sum(l => l.Quantity));
        }

        var dispatches = new List<DispatchDto>();
        foreach (var d in submitted) dispatches.Add((await kit.Admin.GetJsonAsync<DispatchDto>($"/api/v1/dispatches/{d.Id}"))!);
        var approved = dispatches.Where(d => d.Status == Domain.Dispatches.DispatchStatus.Approved).ToList();
        approved.ShouldNotBeEmpty();
        dispatches.Count(d => d.Status == Domain.Dispatches.DispatchStatus.Submitted).ShouldBeGreaterThan(0, "demand exceeds supply, so some approvals must fail");
        await AssertInvariantsAsync(kit);

        // ---- Round 2, all at once: ship half of the approved dispatches (with a duplicate ship each), cancel the rest.
        var shipped = new ConcurrentBag<decimal>();
        await Task.WhenAll(approved.Select(async (d, i) =>
        {
            if (i % 2 == 0)
            {
                await kit.PickAllAsync(d.Id);
                await kit.ReadyAsync(d.Id);
                var key = Guid.NewGuid().ToString();
                var ships = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ =>
                    InventoryKit.PostWithKeyAsync(kit.Manager, $"/api/v1/dispatches/{d.Id}/ship", new { carrier = "Van" }, key)));
                ships.ShouldContain(r => r.StatusCode == HttpStatusCode.OK);
                var after = (await kit.Admin.GetJsonAsync<DispatchDto>($"/api/v1/dispatches/{d.Id}"))!;
                after.Status.ShouldBe(Domain.Dispatches.DispatchStatus.Dispatched);
                shipped.Add(after.Lines.Sum(l => l.QuantityShipped));
            }
            else
            {
                (await kit.Manager.PostJsonAsync($"/api/v1/dispatches/{d.Id}/cancel", new { reason = "Soak test" })).StatusCode.ShouldBe(HttpStatusCode.OK);
            }
        }));
        await AssertInvariantsAsync(kit);

        // ---- Totals: what is on hand equals what the successful requests did.
        var onHand = 0m;
        foreach (var p in products) onHand += await stock.OnHandAsync(p.Id);
        onHand.ShouldBe(Initial() + received.Sum() + adjusted.Sum() - shipped.Sum());
        received.Count.ShouldBe(products.Count); // every receipt posted; a double post would break the total above
        // Nothing is reserved any more: shipped dispatches consumed, cancelled ones released.
        foreach (var p in products) (await stock.BalancesAsync(p.Id)).Sum(b => b.Reserved).ShouldBe(0);
    }

    private static async Task AssertInvariantsAsync(DispatchKit kit)
    {
        var check = (await kit.Admin.GetJsonAsync<ReconciliationResult>($"/api/v1/inventory/reconciliation?warehouseId={kit.WarehouseId}"))!;
        check.Issues.ShouldBeEmpty();
        var balances = (await kit.Admin.GetJsonAsync<Application.Common.PagedResult<BalanceDto>>($"/api/v1/inventory/balances?warehouseId={kit.WarehouseId}&pageSize=200"))!;
        balances.Items.ShouldAllBe(b => b.OnHand >= 0 && b.Reserved >= 0 && b.Reserved <= b.OnHand && b.Available == b.OnHand - b.Reserved);
    }
}
