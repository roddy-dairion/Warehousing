using System.Net;
using System.Net.Http.Headers;
using Inventory.Application.Common;
using Inventory.Application.Products;
using static Inventory.IntegrationTests.Http;

namespace Inventory.IntegrationTests;

public sealed class ProductTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private async Task<(HttpClient Admin, Dictionary<string, Guid> Units)> AdminAsync()
    {
        var (admin, _) = await api.SignInAsync(ApiFactory.AdminEmail);
        var units = (await admin.GetJsonAsync<List<UnitDto>>("/api/v1/units"))!.ToDictionary(u => u.Code, u => u.Id);
        return (admin, units);
    }

    private static object NewProduct(string sku, Dictionary<string, Guid> units, string baseBarcode, string cartonBarcode) => new
    {
        sku,
        name = "Soap bar 100 g",
        baseUnitId = units["EA"],
        minStockQty = 24,
        isLotTracked = true,
        hasExpiry = true,
        shelfLifeDays = 730,
        units = new[] { new { unitId = units["CTN"], factorToBase = 48 } },
        barcodes = new object[] { new { value = baseBarcode }, new { value = cartonBarcode, unitId = units["CTN"] } },
    };

    [Fact]
    public async Task Create_product_with_pack_and_barcodes_then_scan_both()
    {
        var (admin, units) = await AdminAsync();
        var (ea, ctn) = (Unique("EAN"), Unique("CTN"));
        var p = await (await admin.PostJsonAsync("/api/v1/products", NewProduct(Unique("soap"), units, ea, ctn))).ReadAsync<ProductDto>();

        p.Units.Single().FactorToBase.ShouldBe(48);
        p.Barcodes.Count.ShouldBe(2);
        p.Barcodes.ShouldAllBe(b => b.IsPrimary);

        var each = (await admin.GetJsonAsync<BarcodeLookupDto>($"/api/v1/product-barcodes/{ea}"))!;
        each.ProductId.ShouldBe(p.Id);
        each.UnitCode.ShouldBe("EA");
        each.FactorToBase.ShouldBe(1);

        var carton = (await admin.GetJsonAsync<BarcodeLookupDto>($"/api/v1/product-barcodes/{ctn}"))!;
        carton.UnitCode.ShouldBe("CTN");
        carton.FactorToBase.ShouldBe(48);
        carton.IsLotTracked.ShouldBeTrue();
    }

    [Fact]
    public async Task Unknown_barcode_is_404_and_inactive_products_are_flagged()
    {
        var (admin, units) = await AdminAsync();
        (await admin.GetAsync("/api/v1/product-barcodes/NOT-A-REAL-CODE", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var ea = Unique("EAN");
        var p = await (await admin.PostJsonAsync("/api/v1/products", NewProduct(Unique("old"), units, ea, Unique("C")))).ReadAsync<ProductDto>();
        await admin.PostAsync($"/api/v1/products/{p.Id}/deactivate");
        (await admin.GetJsonAsync<BarcodeLookupDto>($"/api/v1/product-barcodes/{ea}"))!.IsActive.ShouldBeFalse();
    }

    [Fact]
    public async Task Barcodes_and_skus_are_unique_across_products()
    {
        var (admin, units) = await AdminAsync();
        var (sku, ea) = (Unique("u"), Unique("EAN"));
        (await admin.PostJsonAsync("/api/v1/products", NewProduct(sku, units, ea, Unique("C")))).EnsureSuccessStatusCode();

        (await (await admin.PostJsonAsync("/api/v1/products", NewProduct(sku.ToLowerInvariant(), units, Unique("E"), Unique("C")))).ProblemAsync(HttpStatusCode.Conflict))
            .Code().ShouldBe("product.sku_taken");
        (await (await admin.PostJsonAsync("/api/v1/products", NewProduct(Unique("v"), units, ea, Unique("C")))).ProblemAsync(HttpStatusCode.Conflict))
            .Code().ShouldBe("barcode.in_use");
    }

    [Fact]
    public async Task Stock_rules_are_validated()
    {
        var (admin, units) = await AdminAsync();
        var expiryWithoutLot = await admin.PostJsonAsync("/api/v1/products", new { sku = Unique("e"), name = "Milk", baseUnitId = units["EA"], hasExpiry = true });
        (await expiryWithoutLot.ProblemAsync(HttpStatusCode.UnprocessableEntity)).Code().ShouldBe("product.expiry_needs_lot");

        var fractionalEach = await admin.PostJsonAsync("/api/v1/products", new { sku = Unique("f"), name = "Pen", baseUnitId = units["EA"], minStockQty = 2.5 });
        (await fractionalEach.ProblemAsync(HttpStatusCode.UnprocessableEntity)).Code().ShouldBe("product.min_stock_precision");

        var kg = await admin.PostJsonAsync("/api/v1/products", new { sku = Unique("k"), name = "Rice", baseUnitId = units["KG"], minStockQty = 2.5 });
        kg.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task A_pack_unit_with_barcodes_cannot_be_removed()
    {
        var (admin, units) = await AdminAsync();
        var p = await (await admin.PostJsonAsync("/api/v1/products", NewProduct(Unique("pk"), units, Unique("E"), Unique("C")))).ReadAsync<ProductDto>();
        var pack = p.Units.Single();
        (await (await admin.DeleteAsync($"/api/v1/products/{p.Id}/units/{pack.Id}?version={p.Version}", Ct)).ProblemAsync(HttpStatusCode.Conflict))
            .Code().ShouldBe("product.unit_has_barcodes");

        var cartonBarcode = p.Barcodes.Single(b => b.UnitCode == "CTN");
        p = await (await admin.DeleteAsync($"/api/v1/products/{p.Id}/barcodes/{cartonBarcode.Id}?version={p.Version}", Ct)).ReadAsync<ProductDto>();
        p = await (await admin.DeleteAsync($"/api/v1/products/{p.Id}/units/{pack.Id}?version={p.Version}", Ct)).ReadAsync<ProductDto>();
        p.Units.ShouldBeEmpty();
    }

    [Fact]
    public async Task Concurrent_edits_with_the_same_version_conflict()
    {
        var (admin, units) = await AdminAsync();
        var p = await (await admin.PostJsonAsync("/api/v1/products", NewProduct(Unique("cc"), units, Unique("E"), Unique("C")))).ReadAsync<ProductDto>();
        var update = new { name = "Renamed", baseUnitId = units["EA"], isLotTracked = true, hasExpiry = true, version = p.Version };

        (await admin.PutJsonAsync($"/api/v1/products/{p.Id}", update)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await admin.PutJsonAsync($"/api/v1/products/{p.Id}", update)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        // A child change (barcode) also bumps the version.
        (await admin.PostJsonAsync($"/api/v1/products/{p.Id}/barcodes", new { value = Unique("N"), version = p.Version })).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Search_matches_sku_name_or_exact_barcode()
    {
        var (admin, units) = await AdminAsync();
        var (sku, ea) = (Unique("find"), Unique("EAN"));
        (await admin.PostJsonAsync("/api/v1/products", NewProduct(sku, units, ea, Unique("C")))).EnsureSuccessStatusCode();

        (await admin.GetJsonAsync<PagedResult<ProductListItemDto>>($"/api/v1/products?search={sku.ToLowerInvariant()}"))!.Items.Single().Sku.ShouldBe(sku);
        (await admin.GetJsonAsync<PagedResult<ProductListItemDto>>($"/api/v1/products?search={ea}"))!.Items.Single().PrimaryBarcode.ShouldBe(ea);
    }

    [Fact]
    public async Task Operators_can_read_and_scan_but_not_edit_products()
    {
        var (_, units) = await AdminAsync();
        var (op, _) = await api.SignInAsync(await api.CreateUserAsync(RoleCodes.Operator, await api.MainWarehouseIdAsync()));
        (await op.GetAsync("/api/v1/products", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await op.PostJsonAsync("/api/v1/products", NewProduct(Unique("no"), units, Unique("E"), Unique("C")))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await op.PostJsonAsync("/api/v1/units", new { code = "XX", name = "X", decimals = 0 })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Image_upload_checks_the_real_file_type()
    {
        var (admin, units) = await AdminAsync();
        var p = await (await admin.PostJsonAsync("/api/v1/products", NewProduct(Unique("img"), units, Unique("E"), Unique("C")))).ReadAsync<ProductDto>();

        // A text file pretending to be a PNG is rejected.
        var fake = new MultipartFormDataContent { { new ByteArrayContent("not an image"u8.ToArray()) { Headers = { ContentType = new MediaTypeHeaderValue("image/png") } }, "file", "photo.png" } };
        (await (await admin.PutAsync($"/api/v1/products/{p.Id}/image", fake, Ct)).ProblemAsync(HttpStatusCode.UnprocessableEntity)).Code().ShouldBe("image.type");

        // A real PNG (1x1) is stored and served back to authorised users only.
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=");
        var real = new MultipartFormDataContent { { new ByteArrayContent(png) { Headers = { ContentType = new MediaTypeHeaderValue("image/png") } }, "file", "photo.png" } };
        (await admin.PutAsync($"/api/v1/products/{p.Id}/image", real, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var image = await admin.GetAsync($"/api/v1/products/{p.Id}/image", Ct);
        image.Content.Headers.ContentType!.MediaType.ShouldBe("image/png");
        (await image.Content.ReadAsByteArrayAsync(Ct)).ShouldBe(png);
        (await admin.GetJsonAsync<ProductDto>($"/api/v1/products/{p.Id}"))!.HasImage.ShouldBeTrue();
        (await api.CreateClient().GetAsync($"/api/v1/products/{p.Id}/image", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        (await admin.DeleteAsync($"/api/v1/products/{p.Id}/image", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await admin.GetAsync($"/api/v1/products/{p.Id}/image", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Categories_cannot_form_a_cycle()
    {
        var (admin, _) = await AdminAsync();
        var parent = await (await admin.PostJsonAsync("/api/v1/categories", new { code = Unique("P"), name = "Parent" })).ReadAsync<CategoryDto>();
        var child = await (await admin.PostJsonAsync("/api/v1/categories", new { code = Unique("C"), name = "Child", parentId = parent.Id })).ReadAsync<CategoryDto>();
        child.Path.ShouldBe("Parent › Child");

        (await (await admin.PutJsonAsync($"/api/v1/categories/{parent.Id}", new { name = "Parent", parentId = child.Id, version = parent.Version }))
            .ProblemAsync(HttpStatusCode.UnprocessableEntity)).Code().ShouldBe("category.cycle");
    }

    [Fact]
    public async Task Standard_units_are_seeded()
    {
        var (_, units) = await AdminAsync();
        units.Keys.ShouldContain("EA");
        units.Keys.ShouldContain("KG");
        units.Keys.ShouldContain("CTN");
    }
}
