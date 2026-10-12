using System.Net;
using Inventory.Application.Dispatches;
using Inventory.Domain.Dispatches;
using static Inventory.IntegrationTests.Http;

namespace Inventory.IntegrationTests;

public sealed class DispatchConcurrencyTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Competing_approvals_for_the_same_stock_cannot_both_succeed()
    {
        var kit = await DispatchKit.CreateAsync(api);
        var customer = await kit.CustomerAsync();
        var (p, _, _) = await kit.StockedProductAsync(10);
        var dispatches = new List<DispatchDto>();
        for (var i = 0; i < 4; i++) dispatches.Add(await kit.SubmittedAsync(customer.Id, DispatchKit.Line(p.Id, 4)));

        var responses = await Task.WhenAll(dispatches.Select(d => kit.Manager.PostJsonAsync($"/api/v1/dispatches/{d.Id}/approve", new { })));
        responses.Count(r => r.StatusCode == HttpStatusCode.OK).ShouldBe(2);
        responses.Count(r => r.StatusCode == HttpStatusCode.Conflict).ShouldBe(2);
        var b = (await kit.Stock.BalancesAsync(p.Id)).Single();
        (b.OnHand, b.Reserved).ShouldBe((10m, 8m));
    }

    [Fact]
    public async Task Concurrent_shipment_confirmations_deduct_stock_once()
    {
        var kit = await DispatchKit.CreateAsync(api);
        var customer = await kit.CustomerAsync();
        var (p, _, _) = await kit.StockedProductAsync(10);
        var d = await kit.ApprovedAsync(customer.Id, DispatchKit.Line(p.Id, 6));
        await kit.PickAllAsync(d.Id);
        await kit.ReadyAsync(d.Id);

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => kit.Manager.PostJsonAsync($"/api/v1/dispatches/{d.Id}/ship", new { })));
        responses.Count(r => r.StatusCode == HttpStatusCode.OK).ShouldBe(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.Conflict).ShouldBe(5);
        var b = (await kit.Stock.BalancesAsync(p.Id)).Single();
        (b.OnHand, b.Reserved).ShouldBe((4m, 0m));
        (await kit.Stock.MovementCountAsync(p.Id)).ShouldBe(2); // receipt + one dispatch
    }

    [Fact]
    public async Task Ship_with_idempotency_key_replays_and_parallel_scans_with_one_id_count_once()
    {
        var kit = await DispatchKit.CreateAsync(api);
        var customer = await kit.CustomerAsync();
        var (p, _, barcode) = await kit.StockedProductAsync(10);
        var d = await kit.ApprovedAsync(customer.Id, DispatchKit.Line(p.Id, 5));
        var bin = (await kit.PickListAsync(d.Id)).Tasks.Single().LocationCode;

        var scanId = Guid.NewGuid().ToString();
        var scans = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => kit.ScanAsync(d.Id, bin, barcode, 5, scanId: scanId)));
        scans.ShouldAllBe(r => r.StatusCode == HttpStatusCode.OK);
        (await kit.Operator.GetJsonAsync<DispatchDto>($"/api/v1/dispatches/{d.Id}"))!.Lines.Single().QuantityPicked.ShouldBe(5);

        await kit.ReadyAsync(d.Id);
        var key = Guid.NewGuid().ToString();
        var first = await InventoryKit.PostWithKeyAsync(kit.Manager, $"/api/v1/dispatches/{d.Id}/ship", new { carrier = "Van" }, key);
        var second = await InventoryKit.PostWithKeyAsync(kit.Manager, $"/api/v1/dispatches/{d.Id}/ship", new { carrier = "Van" }, key);
        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        second.StatusCode.ShouldBe(HttpStatusCode.OK);
        second.Headers.Contains("Idempotent-Replayed").ShouldBeTrue();
        (await second.ReadAsync<DispatchDto>()).Status.ShouldBe(DispatchStatus.Dispatched);
        (await kit.Stock.OnHandAsync(p.Id)).ShouldBe(5);
    }
}
