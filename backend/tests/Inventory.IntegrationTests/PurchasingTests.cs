using System.Net;
using Inventory.Application.Common;
using Inventory.Application.Inventory;
using Inventory.Application.Purchasing;
using Inventory.Application.Products;
using Inventory.Application.Reporting;
using Inventory.Domain.Inventory;
using Inventory.Domain.Purchasing;
using Inventory.Domain.Security;
using Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Inventory.IntegrationTests.Http;

namespace Inventory.IntegrationTests;

public sealed class PurchasingTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private static async Task<SupplierDto> SupplierAsync(InventoryKit kit, int? leadTime = 5) =>
        await (await kit.Admin.PostJsonAsync("/api/v1/suppliers", new { code = Unique("SUP"), name = "Supplier " + Unique("S"), leadTimeDays = leadTime }))
            .ReadAsync<SupplierDto>();

    private static Task<HttpResponseMessage> CreateOrderAsync(HttpClient client, InventoryKit kit, Guid supplierId, params object[] lines) =>
        client.PostJsonAsync("/api/v1/purchase-orders", new { warehouseId = kit.WarehouseId, supplierId, lines });

    private static object OrderLine(Guid productId, decimal quantity, Guid? productUnitId = null) => new { productId, quantity, productUnitId };

    private static async Task<PurchaseOrderDto> PlacedOrderAsync(InventoryKit kit, Guid supplierId, params object[] lines)
    {
        var draft = await (await CreateOrderAsync(kit.Admin, kit, supplierId, lines)).ReadAsync<PurchaseOrderDto>();
        return await (await kit.Admin.PostAsync($"/api/v1/purchase-orders/{draft.Id}/place")).ReadAsync<PurchaseOrderDto>();
    }

    /// <summary>The product's stock overview row (products with neither stock, minimum nor anything on order are not listed).</summary>
    private static async Task<StockSummaryDto> SummaryAsync(InventoryKit kit, ProductDto p) =>
        (await kit.Admin.GetJsonAsync<PagedResult<StockSummaryDto>>($"/api/v1/inventory/summary?warehouseId={kit.WarehouseId}&search={p.Sku}"))!
            .Items.SingleOrDefault(s => s.ProductId == p.Id) ?? new StockSummaryDto { ProductId = p.Id };

    private static Task<PurchaseOrderDto> GetOrderAsync(InventoryKit kit, Guid id) => kit.Admin.GetJsonAsync<PurchaseOrderDto>($"/api/v1/purchase-orders/{id}")!;

    [Fact]
    public async Task Ordering_counts_as_on_order_and_receiving_against_the_order_records_what_arrived()
    {
        var kit = await InventoryKit.CreateAsync(api);
        var supplier = await SupplierAsync(kit, leadTime: 5);
        var cartons = await kit.ProductAsync(cartonOf: 12);
        var lots = await kit.ProductAsync(lots: true);
        var ctn = cartons.Units.Single().Id;

        var draft = await (await CreateOrderAsync(kit.Admin, kit, supplier.Id, OrderLine(cartons.Id, 10, ctn), OrderLine(lots.Id, 30))).ReadAsync<PurchaseOrderDto>();
        draft.Number.ShouldMatch(@"^PO-MAIN-\d{4}-\d{6}$");
        draft.Status.ShouldBe(PurchaseOrderStatus.Draft);
        draft.ExpectedDate.ShouldBe(draft.OrderDate.AddDays(5));
        draft.Lines[0].Quantity.ShouldBe(120);
        draft.Lines[0].UnitCode.ShouldBe("CTN");
        (await SummaryAsync(kit, cartons)).OnOrder.ShouldBe(0, "a draft is not on order yet");

        var placed = await (await kit.Admin.PostAsync($"/api/v1/purchase-orders/{draft.Id}/place")).ReadAsync<PurchaseOrderDto>();
        placed.Status.ShouldBe(PurchaseOrderStatus.Ordered);
        placed.OrderedByName.ShouldNotBeNull();
        (await SummaryAsync(kit, cartons)).OnOrder.ShouldBe(120);
        (await kit.OnHandAsync(cartons.Id)).ShouldBe(0, "ordering never changes stock");
        var dashboard = (await kit.Admin.GetJsonAsync<WarehouseDashboardDto>($"/api/v1/dashboard/warehouse?warehouseId={kit.WarehouseId}"))!;
        dashboard.Purchasing.ShouldNotBeNull().Open.ShouldBeGreaterThanOrEqualTo(1);

        // A draft receipt for everything outstanding: cartons stay in cartons, lots are filled in before posting.
        var receipt = await (await kit.Admin.PostAsync($"/api/v1/purchase-orders/{placed.Id}/receipts")).ReadAsync<ReceiptDto>();
        receipt.PurchaseOrderId.ShouldBe(placed.Id);
        receipt.PurchaseOrderNumber.ShouldBe(placed.Number);
        receipt.SupplierName.ShouldBe(supplier.Name);
        receipt.Lines.Count.ShouldBe(2);
        receipt.Lines[0].UnitCode.ShouldBe("CTN");
        receipt.Lines[0].EnteredQuantity.ShouldBe(10);
        receipt.Lines[0].ExpectedQuantity.ShouldBe(10);
        receipt.Lines[1].LotNo.ShouldBeNull();
        receipt.Lines.ShouldAllBe(l => l.PurchaseOrderLineId != null);

        (await (await kit.Admin.PostAsync($"/api/v1/receipts/{receipt.Id}/post")).ProblemAsync(HttpStatusCode.UnprocessableEntity)).Code().ShouldBe("lot.required");

        // Only 8 of 10 cartons arrived; enter the lot and post.
        var bin = await kit.BinAsync();
        var update = new
        {
            receiptDate = receipt.ReceiptDate, supplierName = receipt.SupplierName, version = receipt.Version,
            lines = new[]
            {
                new { productId = cartons.Id, productUnitId = (Guid?)ctn, quantity = 8m, expectedQuantity = (decimal?)10m, locationId = (Guid?)bin, lotNo = (string?)null },
                new { productId = lots.Id, productUnitId = (Guid?)null, quantity = 30m, expectedQuantity = (decimal?)30m, locationId = (Guid?)bin, lotNo = (string?)"L-1" },
            },
        };
        receipt = await (await kit.Admin.PutJsonAsync($"/api/v1/receipts/{receipt.Id}", update)).ReadAsync<ReceiptDto>();
        receipt.Lines.ShouldAllBe(l => l.PurchaseOrderLineId != null, "lines are matched to the order by product");
        await (await kit.Admin.PostAsync($"/api/v1/receipts/{receipt.Id}/post")).ReadAsync<ReceiptDto>();

        var order = await GetOrderAsync(kit, placed.Id);
        order.Status.ShouldBe(PurchaseOrderStatus.PartiallyReceived);
        order.Lines[0].ReceivedQuantity.ShouldBe(96);
        order.Lines[0].Outstanding.ShouldBe(24);
        order.Lines[1].Outstanding.ShouldBe(0);
        order.Receipts.ShouldHaveSingleItem().Status.ShouldBe(ReceiptStatus.Posted);
        (await SummaryAsync(kit, cartons)).OnOrder.ShouldBe(24);
        (await kit.OnHandAsync(cartons.Id)).ShouldBe(96);

        // The rest arrives: the order is complete.
        var second = await (await kit.Admin.PostAsync($"/api/v1/purchase-orders/{placed.Id}/receipts")).ReadAsync<ReceiptDto>();
        second.Lines.ShouldHaveSingleItem().EnteredQuantity.ShouldBe(2);
        await (await kit.Admin.PostAsync($"/api/v1/receipts/{second.Id}/post")).ReadAsync<ReceiptDto>();
        (await GetOrderAsync(kit, placed.Id)).Status.ShouldBe(PurchaseOrderStatus.Received);
        (await (await kit.Admin.PostAsync($"/api/v1/purchase-orders/{placed.Id}/receipts")).ProblemAsync(HttpStatusCode.Conflict))
            .Code().ShouldBe("purchase_order.nothing_outstanding");

        // Reversing a receipt takes its quantities off the order again.
        await (await kit.Admin.PostJsonAsync($"/api/v1/receipts/{second.Id}/reverse", new { reason = "Wrong delivery" })).ReadAsync<ReceiptDto>();
        order = await GetOrderAsync(kit, placed.Id);
        order.Status.ShouldBe(PurchaseOrderStatus.PartiallyReceived);
        order.Lines[0].ReceivedQuantity.ShouldBe(96);
        (await SummaryAsync(kit, cartons)).OnOrder.ShouldBe(24);
    }

    [Fact]
    public async Task Drafts_can_change_freely_placed_orders_only_their_dates_and_finished_orders_not_at_all()
    {
        var kit = await InventoryKit.CreateAsync(api);
        var supplier = await SupplierAsync(kit, leadTime: null);
        var p = await kit.ProductAsync();
        var other = await kit.ProductAsync();

        (await (await CreateOrderAsync(kit.Admin, kit, supplier.Id, OrderLine(p.Id, 5), OrderLine(p.Id, 3))).ProblemAsync(HttpStatusCode.UnprocessableEntity))
            .Code().ShouldBe("purchase_order.duplicate_product");

        var empty = await (await CreateOrderAsync(kit.Admin, kit, supplier.Id)).ReadAsync<PurchaseOrderDto>();
        empty.ExpectedDate.ShouldBeNull();
        (await (await kit.Admin.PostAsync($"/api/v1/purchase-orders/{empty.Id}/place")).ProblemAsync(HttpStatusCode.UnprocessableEntity)).Code().ShouldBe("purchase_order.empty");
        var cancelled = await (await kit.Admin.PostJsonAsync($"/api/v1/purchase-orders/{empty.Id}/cancel", new { reason = (string?)null })).ReadAsync<PurchaseOrderDto>();
        cancelled.Status.ShouldBe(PurchaseOrderStatus.Cancelled);

        var order = await PlacedOrderAsync(kit, supplier.Id, OrderLine(p.Id, 5));
        var changeLines = new { supplierId = supplier.Id, orderDate = order.OrderDate, expectedDate = order.OrderDate.AddDays(3), version = order.Version,
            lines = new[] { OrderLine(other.Id, 1) } };
        (await (await kit.Admin.PutJsonAsync($"/api/v1/purchase-orders/{order.Id}", changeLines)).ProblemAsync(HttpStatusCode.Conflict)).Code().ShouldBe("purchase_order.not_draft");
        var newDate = new { supplierId = supplier.Id, orderDate = order.OrderDate, expectedDate = order.OrderDate.AddDays(3), supplierReference = "CONF-1",
            version = order.Version, lines = (object[]?)null };
        var dated = await (await kit.Admin.PutJsonAsync($"/api/v1/purchase-orders/{order.Id}", newDate)).ReadAsync<PurchaseOrderDto>();
        dated.ExpectedDate.ShouldBe(order.OrderDate.AddDays(3));
        dated.SupplierReference.ShouldBe("CONF-1");
        dated.Lines.ShouldHaveSingleItem().ProductId.ShouldBe(p.Id);

        // A receipt against it, then: no cancelling, closing needs a reason, and nothing more can be received.
        var bin = await kit.BinAsync();
        var r = await (await kit.Admin.PostJsonAsync("/api/v1/receipts",
            new { warehouseId = kit.WarehouseId, purchaseOrderId = order.Id, lines = new[] { InventoryKit.Line(p.Id, 2, bin), InventoryKit.Line(other.Id, 1, bin) } }))
            .ReadAsync<ReceiptDto>();
        r.Lines.Single(l => l.ProductId == other.Id).PurchaseOrderLineId.ShouldBeNull("products not on the order can still be received");
        (await (await kit.Admin.PostJsonAsync($"/api/v1/purchase-orders/{order.Id}/close", new { reason = "Supplier out of stock" })).ProblemAsync(HttpStatusCode.Conflict))
            .Code().ShouldBe("purchase_order.draft_receipt");
        await (await kit.Admin.PostAsync($"/api/v1/receipts/{r.Id}/post")).ReadAsync<ReceiptDto>();
        (await GetOrderAsync(kit, order.Id)).Status.ShouldBe(PurchaseOrderStatus.PartiallyReceived);

        (await (await kit.Admin.PostJsonAsync($"/api/v1/purchase-orders/{order.Id}/cancel", new { reason = "x" })).ProblemAsync(HttpStatusCode.Conflict))
            .Code().ShouldBe("purchase_order.cannot_cancel");
        (await (await kit.Admin.PostJsonAsync($"/api/v1/purchase-orders/{order.Id}/close", new { reason = "" })).ProblemAsync(HttpStatusCode.UnprocessableEntity))
            .Code().ShouldBe("purchase_order.reason_required");
        var closed = await (await kit.Admin.PostJsonAsync($"/api/v1/purchase-orders/{order.Id}/close", new { reason = "Supplier out of stock" })).ReadAsync<PurchaseOrderDto>();
        closed.Status.ShouldBe(PurchaseOrderStatus.Closed);
        closed.CloseReason.ShouldBe("Supplier out of stock");
        closed.Lines[0].ReceivedQuantity.ShouldBe(2);
        (await SummaryAsync(kit, p)).OnOrder.ShouldBe(0);

        (await (await kit.Admin.PostJsonAsync("/api/v1/receipts", new { warehouseId = kit.WarehouseId, purchaseOrderId = order.Id, lines = new[] { InventoryKit.Line(p.Id, 1, bin) } }))
            .ProblemAsync(HttpStatusCode.Conflict)).Code().ShouldBe("purchase_order.not_receivable");
        var newDate2 = new { supplierId = supplier.Id, orderDate = order.OrderDate, expectedDate = (DateOnly?)null, version = closed.Version, lines = (object[]?)null };
        (await (await kit.Admin.PutJsonAsync($"/api/v1/purchase-orders/{order.Id}", newDate2)).ProblemAsync(HttpStatusCode.Conflict)).Code().ShouldBe("purchase_order.not_draft");

        // Inactive suppliers cannot be used for new orders.
        (await kit.Admin.PostAsync($"/api/v1/suppliers/{supplier.Id}/deactivate")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await (await CreateOrderAsync(kit.Admin, kit, supplier.Id, OrderLine(p.Id, 1))).ProblemAsync(HttpStatusCode.UnprocessableEntity)).Code().ShouldBe("supplier.inactive");
    }

    [Fact]
    public async Task Suggestions_order_up_to_the_target_in_whole_purchase_units_minus_what_is_already_on_order()
    {
        var kit = await InventoryKit.CreateAsync(api);
        var supplier = await SupplierAsync(kit, leadTime: 7);
        var p = await kit.ProductAsync(minStock: 100, cartonOf: 12);
        var update = new
        {
            name = p.Name, baseUnitId = p.BaseUnitId, minStockQty = 100m, isLotTracked = false, hasExpiry = false, version = p.Version,
            maxStockQty = 300m, defaultSupplierId = supplier.Id, purchaseUnitId = kit.Units["CTN"],
        };
        p = await (await kit.Admin.PutJsonAsync($"/api/v1/products/{p.Id}", update)).ReadAsync<ProductDto>();
        p.MaxStockQty.ShouldBe(300);
        p.DefaultSupplierName.ShouldBe(supplier.Name);
        p.PurchaseUnitId.ShouldBe(kit.Units["CTN"]);
        var noSupplier = await kit.ProductAsync(minStock: 10);
        var enough = await kit.ProductAsync(minStock: 5);

        var bin = await kit.BinAsync();
        await kit.ReceiveAsync(InventoryKit.Line(p.Id, 60, bin), InventoryKit.Line(enough.Id, 5, bin));
        await kit.ReserveAsync(Guid.NewGuid(), [new ReservationRequest(Guid.NewGuid(), p.Id, 10)]);

        async Task<List<ReorderSuggestionDto>> SuggestionsAsync() =>
            (await kit.Admin.GetJsonAsync<List<ReorderSuggestionDto>>($"/api/v1/purchasing/suggestions?warehouseId={kit.WarehouseId}"))!;

        var list = await SuggestionsAsync();
        list.ShouldNotContain(s => s.ProductId == enough.Id, "available 5 is not below the minimum 5");
        var s = list.Single(x => x.ProductId == p.Id);
        s.Available.ShouldBe(50);
        s.OnOrder.ShouldBe(0);
        s.SuggestedPurchaseQuantity.ShouldBe(21, "300 − 50 = 250 EA, rounded up to 21 cartons");
        s.SuggestedQuantity.ShouldBe(252);
        s.PurchaseUnitCode.ShouldBe("CTN");
        s.SupplierId.ShouldBe(supplier.Id);
        s.SupplierLeadTimeDays.ShouldBe(7);
        var n = list.Single(x => x.ProductId == noSupplier.Id);
        n.SupplierId.ShouldBeNull();
        n.SuggestedQuantity.ShouldBe(10, "without an order-up-to level, order up to the minimum");

        var order = await PlacedOrderAsync(kit, supplier.Id, OrderLine(p.Id, 10, p.Units.Single().Id));
        s = (await SuggestionsAsync()).Single(x => x.ProductId == p.Id);
        s.OnOrder.ShouldBe(120);
        s.SuggestedPurchaseQuantity.ShouldBe(11, "300 − 50 − 120 = 130 EA → 11 cartons");
        s.OpenOrderNumbers.ShouldBe([order.Number]);

        var big = await PlacedOrderAsync(kit, supplier.Id, OrderLine(p.Id, 20, p.Units.Single().Id));
        s = (await SuggestionsAsync()).Single(x => x.ProductId == p.Id);
        s.SuggestedQuantity.ShouldBe(0, "still below the minimum, but covered by the open orders");
        s.OpenOrderNumbers.Count.ShouldBe(2);
        big.Number.ShouldNotBe(order.Number);

        // Validation of the reorder settings.
        (await (await kit.Admin.PutJsonAsync($"/api/v1/products/{p.Id}", new
        {
            name = p.Name, baseUnitId = p.BaseUnitId, minStockQty = 100m, isLotTracked = false, hasExpiry = false, version = p.Version, maxStockQty = 50m,
        })).ProblemAsync(HttpStatusCode.UnprocessableEntity)).Code().ShouldBe("product.max_below_min");
        (await (await kit.Admin.PutJsonAsync($"/api/v1/products/{p.Id}", new
        {
            name = p.Name, baseUnitId = p.BaseUnitId, minStockQty = 100m, isLotTracked = false, hasExpiry = false, version = p.Version, purchaseUnitId = kit.Units["PAL"],
        })).ProblemAsync(HttpStatusCode.UnprocessableEntity)).Code().ShouldBe("product.purchase_unit_invalid");
    }

    [Fact]
    public async Task Low_stock_email_is_sent_once_a_day_from_seven_when_recipients_are_set()
    {
        var kit = await InventoryKit.CreateAsync(api);
        await kit.ProductAsync(minStock: 3);

        (await (await kit.Admin.PutJsonAsync("/api/v1/purchasing/settings", new { lowStockAlertEmails = "buyer@test.local, not-an-email" }))
            .ProblemAsync(HttpStatusCode.UnprocessableEntity)).Code().ShouldBe("purchasing.alert_email_invalid");
        var settings = await (await kit.Admin.PutJsonAsync("/api/v1/purchasing/settings", new { lowStockAlertEmails = "buyer@test.local; boss@test.local" }))
            .ReadAsync<PurchasingSettingsDto>();
        settings.LowStockAlertEmails.ShouldBe("buyer@test.local, boss@test.local");

        // Company time zone is Indian/Mauritius (UTC+4).
        Task<bool> RunAtAsync(int year, int month, int day, int utcHour) => api.WithDbAsync((db, sp) =>
            new ReorderService(db, sp.GetRequiredService<IWarehouseAccess>(), new FixedTime(new DateTimeOffset(year, month, day, utcHour, 0, 0, TimeSpan.Zero)),
                sp.GetRequiredService<IAuditWriter>()).SendDailyAlertAsync(Ct));
        Task<int> EmailsAsync() => api.WithDbAsync((db, _) => db.EmailMessages.CountAsync(m => m.RelatedType == "low_stock"));

        (await RunAtAsync(2030, 3, 4, 2)).ShouldBeFalse("06:00 local is too early");
        (await RunAtAsync(2030, 3, 4, 4)).ShouldBeTrue("08:00 local");
        (await RunAtAsync(2030, 3, 4, 9)).ShouldBeFalse("already sent today");
        (await EmailsAsync()).ShouldBe(1);
        var email = await api.WithDbAsync((db, _) => db.EmailMessages.SingleAsync(m => m.RelatedType == "low_stock"));
        email.To.ShouldBe("buyer@test.local, boss@test.local");
        email.Subject.ShouldStartWith("Low stock:");

        await kit.Admin.PutJsonAsync("/api/v1/purchasing/settings", new { lowStockAlertEmails = "" });
        (await RunAtAsync(2030, 3, 5, 4)).ShouldBeFalse("no recipients: no email");
    }

    [Fact]
    public async Task Operators_see_orders_and_receive_against_them_but_only_purchasing_managers_order()
    {
        var kit = await InventoryKit.CreateAsync(api);
        var supplier = await SupplierAsync(kit);
        var p = await kit.ProductAsync();
        var (op, _) = await api.SignInAsync(await api.CreateUserAsync(RoleCodes.Operator, kit.WarehouseId));
        var (manager, _) = await api.SignInAsync(await api.CreateUserAsync(RoleCodes.Manager, kit.WarehouseId));

        (await CreateOrderAsync(op, kit, supplier.Id, OrderLine(p.Id, 4))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await op.PostJsonAsync("/api/v1/suppliers", new { code = Unique("S"), name = "x" })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var order = await (await CreateOrderAsync(manager, kit, supplier.Id, OrderLine(p.Id, 4))).ReadAsync<PurchaseOrderDto>();
        await (await manager.PostAsync($"/api/v1/purchase-orders/{order.Id}/place")).ReadAsync<PurchaseOrderDto>();

        var open = (await op.GetJsonAsync<PagedResult<PurchaseOrderListItemDto>>($"/api/v1/purchase-orders?warehouseId={kit.WarehouseId}&openOnly=true"))!;
        open.Items.ShouldContain(o => o.Id == order.Id);
        var receipt = await (await op.PostAsync($"/api/v1/purchase-orders/{order.Id}/receipts")).ReadAsync<ReceiptDto>();
        receipt.Status.ShouldBe(ReceiptStatus.Draft);
        (await op.PostAsync($"/api/v1/receipts/{receipt.Id}/post")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        await (await manager.PostAsync($"/api/v1/receipts/{receipt.Id}/post")).ReadAsync<ReceiptDto>();
        (await GetOrderAsync(kit, order.Id)).Status.ShouldBe(PurchaseOrderStatus.Received);
    }

    [Fact]
    public async Task Concurrent_receipts_for_the_same_order_both_count()
    {
        var kit = await InventoryKit.CreateAsync(api);
        var supplier = await SupplierAsync(kit);
        var p = await kit.ProductAsync();
        var bin = await kit.BinAsync();
        var order = await PlacedOrderAsync(kit, supplier.Id, OrderLine(p.Id, 100));

        var drafts = new List<ReceiptDto>();
        for (var i = 0; i < 4; i++)
        {
            drafts.Add(await (await kit.Admin.PostJsonAsync("/api/v1/receipts",
                new { warehouseId = kit.WarehouseId, purchaseOrderId = order.Id, lines = new[] { InventoryKit.Line(p.Id, 10, bin) } })).ReadAsync<ReceiptDto>());
        }
        var results = await Task.WhenAll(drafts.Select(d => kit.Admin.PostAsync($"/api/v1/receipts/{d.Id}/post")));
        results.ShouldAllBe(r => r.IsSuccessStatusCode);
        var after = await GetOrderAsync(kit, order.Id);
        after.Lines.Single().ReceivedQuantity.ShouldBe(40);
        after.Status.ShouldBe(PurchaseOrderStatus.PartiallyReceived);
        (await kit.OnHandAsync(p.Id)).ShouldBe(40);
    }

    [Fact]
    public async Task A_permission_added_by_a_new_release_is_granted_once_to_the_built_in_roles()
    {
        // Simulate a database created before purchasing existed.
        await api.WithDbAsync(async (db, _) =>
        {
            string[] codes = [Permissions.PurchasingRead, Permissions.PurchasingManage];
            await db.RolePermissions.Where(rp => codes.Contains(rp.PermissionCode)).ExecuteDeleteAsync(Ct);
            await db.Permissions.Where(p => codes.Contains(p.Code)).ExecuteDeleteAsync(Ct);
        });
        await api.WithDbAsync((_, sp) => sp.GetRequiredService<DatabaseSeeder>().SeedAsync(Ct));

        var grants = await api.WithDbAsync((db, _) => (from rp in db.RolePermissions
                                                       join r in db.Roles on rp.RoleId equals r.Id
                                                       where rp.PermissionCode.StartsWith("purchasing.")
                                                       select r.Name + ":" + rp.PermissionCode).ToListAsync(Ct));
        grants.Order().ShouldBe([
            "ADMIN:purchasing.manage", "ADMIN:purchasing.read", "WH_MANAGER:purchasing.manage", "WH_MANAGER:purchasing.read", "WH_OPERATOR:purchasing.read",
        ]);

        // Removing it from a role afterwards sticks: the next start-up does not grant it again.
        await api.WithDbAsync(async (db, _) =>
        {
            var op = await db.Roles.SingleAsync(r => r.Name == Roles.WarehouseOperator, Ct);
            await db.RolePermissions.Where(rp => rp.RoleId == op.Id && rp.PermissionCode == Permissions.PurchasingRead).ExecuteDeleteAsync(Ct);
        });
        await api.WithDbAsync((_, sp) => sp.GetRequiredService<DatabaseSeeder>().SeedAsync(Ct));
        (await api.WithDbAsync((db, _) => db.RolePermissions.CountAsync(rp => rp.PermissionCode == Permissions.PurchasingRead, Ct))).ShouldBe(2);

        // Restore the default for the other tests in this class.
        await api.WithDbAsync(async (db, _) =>
        {
            var op = await db.Roles.SingleAsync(r => r.Name == Roles.WarehouseOperator, Ct);
            db.RolePermissions.Add(new Inventory.Infrastructure.Identity.RolePermission { RoleId = op.Id, PermissionCode = Permissions.PurchasingRead });
            await db.SaveChangesAsync(Ct);
        });
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
