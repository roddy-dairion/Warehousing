using System.Net;
using Inventory.Application.Common;
using Inventory.Application.Products;
using Inventory.Application.Warehouses;
using static Inventory.IntegrationTests.Http;

namespace Inventory.IntegrationTests;

public sealed class WarehouseManagementTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Creating_a_warehouse_adds_its_built_in_areas()
    {
        var (admin, _) = await api.SignInAsync(ApiFactory.AdminEmail);
        var code = Unique("WH");
        var created = await (await admin.PostJsonAsync("/api/v1/warehouses", new { code, name = "Branch", city = "Curepipe" })).ReadAsync<WarehouseDto>();
        created.Code.ShouldBe(code);
        created.City.ShouldBe("Curepipe");

        var bins = await admin.GetJsonAsync<PagedResult<LocationDto>>($"/api/v1/warehouses/{created.Id}/locations");
        bins!.Items.Select(b => b.Code).ShouldBe(["DOCK", "QUARANTINE", "RECEIVING"], ignoreOrder: true);
        bins.Items.ShouldAllBe(b => b.IsSystem && !b.IsPickable);
    }

    [Fact]
    public async Task Duplicate_warehouse_code_is_a_conflict()
    {
        var (admin, _) = await api.SignInAsync(ApiFactory.AdminEmail);
        var problem = await (await admin.PostJsonAsync("/api/v1/warehouses", new { code = "MAIN", name = "Again" })).ProblemAsync(HttpStatusCode.Conflict);
        problem.Code().ShouldBe("warehouse.code_taken");
    }

    [Fact]
    public async Task Operators_cannot_create_or_edit_warehouses()
    {
        var main = await api.MainWarehouseIdAsync();
        var (op, _) = await api.SignInAsync(await api.CreateUserAsync(RoleCodes.Operator, main));
        (await op.PostJsonAsync("/api/v1/warehouses", new { code = "NOPE", name = "No" })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await op.PostAsync($"/api/v1/warehouses/{main}/deactivate")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Stale_version_is_rejected_with_409()
    {
        var (admin, _) = await api.SignInAsync(ApiFactory.AdminEmail);
        var id = await api.CreateWarehouseAsync(Unique("V")[..10]);
        var current = (await admin.GetJsonAsync<WarehouseDto>($"/api/v1/warehouses/{id}"))!;

        (await admin.PutJsonAsync($"/api/v1/warehouses/{id}", new { name = "First edit", version = current.Version })).StatusCode.ShouldBe(HttpStatusCode.OK);
        var problem = await (await admin.PutJsonAsync($"/api/v1/warehouses/{id}", new { name = "Second edit", version = current.Version })).ProblemAsync(HttpStatusCode.Conflict);
        problem.Code().ShouldBe("concurrency_conflict");
    }

    [Fact]
    public async Task Bins_follow_the_rules()
    {
        var (admin, _) = await api.SignInAsync(ApiFactory.AdminEmail);
        var wh = await api.CreateWarehouseAsync(Unique("B")[..10]);
        var url = $"/api/v1/warehouses/{wh}/locations";

        var bin = await (await admin.PostJsonAsync(url, new { code = "a-01-01", type = "storage", zone = "A", barcode = "BIN-A0101" })).ReadAsync<LocationDto>();
        bin.Code.ShouldBe("A-01-01");
        bin.IsPickable.ShouldBeTrue();

        (await (await admin.PostJsonAsync(url, new { code = "A-01-01", type = "storage" })).ProblemAsync(HttpStatusCode.Conflict)).Code().ShouldBe("location.code_taken");
        (await (await admin.PostJsonAsync(url, new { code = "Q-2", type = "quarantine", isPickable = true })).ProblemAsync(HttpStatusCode.UnprocessableEntity)).Code().ShouldBe("location.pickable_type");
        (await (await admin.PostJsonAsync(url, new { code = "A-01-02", type = "storage", barcode = "BIN-A0101" })).ProblemAsync(HttpStatusCode.Conflict)).Code().ShouldBe("barcode.in_use");

        var system = (await admin.GetJsonAsync<PagedResult<LocationDto>>($"{url}?search=receiving"))!.Items.Single();
        (await (await admin.PostAsync($"/api/v1/locations/{system.Id}/deactivate")).ProblemAsync(HttpStatusCode.UnprocessableEntity)).Code().ShouldBe("location.system_deactivate");

        (await admin.PostAsync($"/api/v1/locations/{bin.Id}/deactivate")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var inactive = (await admin.GetJsonAsync<PagedResult<LocationDto>>($"{url}?isActive=false"))!.Items;
        inactive.Select(b => b.Code).ShouldBe(["A-01-01"]);
    }

    [Fact]
    public async Task Bins_of_other_warehouses_are_invisible_to_staff()
    {
        var other = await api.CreateWarehouseAsync(Unique("O")[..10]);
        var (op, _) = await api.SignInAsync(await api.CreateUserAsync(RoleCodes.Operator, await api.MainWarehouseIdAsync()));
        (await op.GetAsync($"/api/v1/warehouses/{other}/locations", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_bin_barcode_cannot_reuse_a_product_barcode()
    {
        var (admin, _) = await api.SignInAsync(ApiFactory.AdminEmail);
        var units = (await admin.GetJsonAsync<List<UnitDto>>("/api/v1/units"))!;
        var barcode = Unique("PB");
        await (await admin.PostJsonAsync("/api/v1/products", new
        {
            sku = Unique("SKU"), name = "Thing", baseUnitId = units.Single(u => u.Code == "EA").Id,
            barcodes = new[] { new { value = barcode } },
        })).ReadAsync<ProductDto>();

        var wh = await api.MainWarehouseIdAsync();
        (await (await admin.PostJsonAsync($"/api/v1/warehouses/{wh}/locations", new { code = Unique("X")[..10], type = "storage", barcode }))
            .ProblemAsync(HttpStatusCode.Conflict)).Code().ShouldBe("barcode.in_use");
    }

    [Fact]
    public async Task The_last_active_warehouse_cannot_be_deactivated()
    {
        // In this factory MAIN may not be the only active one, so deactivate all others first.
        var (admin, _) = await api.SignInAsync(ApiFactory.AdminEmail);
        var all = (await admin.GetJsonAsync<List<WarehouseDto>>("/api/v1/warehouses"))!;
        foreach (var w in all.Where(w => w.Code != "MAIN")) await admin.PostAsync($"/api/v1/warehouses/{w.Id}/deactivate");
        var main = all.Single(w => w.Code == "MAIN");

        (await (await admin.PostAsync($"/api/v1/warehouses/{main.Id}/deactivate")).ProblemAsync(HttpStatusCode.Conflict)).Code().ShouldBe("warehouse.last_active");
        foreach (var w in all.Where(w => w.Code != "MAIN")) await admin.PostAsync($"/api/v1/warehouses/{w.Id}/activate");
    }
}
