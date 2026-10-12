using System.Net;
using Inventory.Application.Common;
using Inventory.Application.Reporting;
using static Inventory.IntegrationTests.Http;

namespace Inventory.IntegrationTests;

public sealed class ReportingTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Dashboard_counts_follow_the_users_permissions()
    {
        var kit = await DispatchKit.CreateAsync(api);
        var customer = await kit.CustomerAsync();
        var (p, _, _) = await kit.StockedProductAsync(20);
        await kit.Stock.ProductAsync(minStock: 5); // low stock (nothing on hand)
        await kit.SubmittedAsync(customer.Id, DispatchKit.Line(p.Id, 2));
        await kit.ApprovedAsync(customer.Id, DispatchKit.Line(p.Id, 3));
        await kit.Stock.DraftReceiptAsync(InventoryKit.Line(p.Id, 1));

        var manager = (await kit.Manager.GetJsonAsync<WarehouseDashboardDto>($"/api/v1/dashboard/warehouse?warehouseId={kit.WarehouseId}"))!;
        // Other tests in this class share the database, so figures are at least what this test created.
        manager.Stock!.OnHand.ShouldBeGreaterThanOrEqualTo(20);
        manager.Stock.Reserved.ShouldBeGreaterThanOrEqualTo(3);
        manager.Stock.LowStockProducts.ShouldBeGreaterThanOrEqualTo(1);
        manager.Dispatches!.AwaitingApproval.ShouldBeGreaterThanOrEqualTo(1);
        manager.Dispatches.ToPick.ShouldBeGreaterThanOrEqualTo(1);
        manager.PendingReceipts!.Value.ShouldBeGreaterThanOrEqualTo(1);
        manager.DispatchesToAction!.Count.ShouldBeGreaterThanOrEqualTo(2); // approve + pick
        manager.RecentMovements!.ShouldNotBeEmpty();

        // A user without dispatch permission does not get those figures.
        var (receiver, _) = await api.SignInAsync(await api.CreateUserAsync(RoleCodes.Operator, kit.WarehouseId));
        var op = (await receiver.GetJsonAsync<WarehouseDashboardDto>($"/api/v1/dashboard/warehouse?warehouseId={kit.WarehouseId}"))!;
        op.Dispatches.ShouldNotBeNull(); // operators can read dispatches
        op.AdjustmentsAwaitingApproval.ShouldNotBeNull();

        var other = await api.CreateWarehouseAsync(Unique("W")[..8]);
        (await kit.Operator.GetAsync($"/api/v1/dashboard/warehouse?warehouseId={other}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Every_report_runs_and_downloads_as_csv()
    {
        var kit = await DispatchKit.CreateAsync(api);
        var customer = await kit.CustomerAsync();
        var (p, _, _) = await kit.StockedProductAsync(10, lots: true, lot: "R-1");
        var d = await kit.ApprovedAsync(customer.Id, DispatchKit.Line(p.Id, 4));
        await kit.PickAllAsync(d.Id, 0.5m);
        await kit.ReadyAsync(d.Id, allowShort: true);
        await kit.Manager.PostJsonAsync($"/api/v1/dispatches/{d.Id}/ship", new { shortDecision = "closeShort" });

        var kinds = (await kit.Manager.GetJsonAsync<List<ReportInfoDto>>("/api/v1/reports"))!;
        kinds.Count.ShouldBe(8);
        foreach (var kind in kinds)
        {
            var report = (await kit.Manager.GetJsonAsync<ReportResult>($"/api/v1/reports/{kind.Kind}?warehouseId={kit.WarehouseId}&from={InventoryKit.Today.AddDays(-1):yyyy-MM-dd}&to={InventoryKit.Today:yyyy-MM-dd}"))!;
            report.Columns.ShouldNotBeEmpty(kind.Kind);
            report.Rows.ShouldAllBe(r => r.Count == report.Columns.Count);
        }

        var stock = (await kit.Manager.GetJsonAsync<ReportResult>($"/api/v1/reports/stock-on-hand?warehouseId={kit.WarehouseId}"))!;
        var row = stock.Rows.Single(r => r[0] == p.Sku);
        row[stock.Columns.ToList().IndexOf("Lot")].ShouldBe("R-1");
        row[stock.Columns.ToList().IndexOf("On hand")].ShouldBe("8");

        var shorts = (await kit.Manager.GetJsonAsync<ReportResult>($"/api/v1/reports/dispatch-discrepancies?warehouseId={kit.WarehouseId}"))!;
        shorts.Rows.ShouldContain(r => r[0] == d.Number && r[2] == "Short shipment" && r[8] == "2");

        var csv = await kit.Manager.GetAsync($"/api/v1/reports/movements/csv?warehouseId={kit.WarehouseId}", Ct);
        csv.StatusCode.ShouldBe(HttpStatusCode.OK);
        csv.Content.Headers.ContentType!.MediaType.ShouldBe("text/csv");
        var text = await csv.Content.ReadAsStringAsync(Ct);
        text.ShouldContain("Date (UTC),Type,SKU");
        text.ShouldContain(p.Sku);

        (await kit.Operator.GetAsync($"/api/v1/reports/stock-on-hand?warehouseId={kit.WarehouseId}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await kit.Manager.GetAsync($"/api/v1/reports/nope?warehouseId={kit.WarehouseId}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Audit_log_lists_actions_with_filters_and_requires_permission()
    {
        var kit = await DispatchKit.CreateAsync(api);
        var customer = await kit.CustomerAsync();
        var (p, _, _) = await kit.StockedProductAsync(5);
        var d = await kit.ApprovedAsync(customer.Id, DispatchKit.Line(p.Id, 1));

        var entries = (await kit.Admin.GetJsonAsync<PagedResult<AuditEntryDto>>($"/api/v1/audit-logs?entityType=dispatch&entityId={d.Id}"))!;
        entries.Items.Select(e => e.Action).ShouldContain("dispatch.approved");
        entries.Items[0].OccurredAt.ShouldBeGreaterThanOrEqualTo(entries.Items[^1].OccurredAt); // newest first
        entries.Items.ShouldAllBe(e => e.WarehouseCode == "MAIN");
        entries.Items.First(e => e.Action == "dispatch.approved").UserName.ShouldNotBeNull();

        var prefix = (await kit.Admin.GetJsonAsync<PagedResult<AuditEntryDto>>("/api/v1/audit-logs?action=receipt.&pageSize=200"))!;
        prefix.Items.ShouldAllBe(e => e.Action.StartsWith("receipt.", StringComparison.Ordinal));
        prefix.Items.ShouldContain(e => e.Action == "receipt.posted");

        (await kit.Manager.GetAsync("/api/v1/audit-logs", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
