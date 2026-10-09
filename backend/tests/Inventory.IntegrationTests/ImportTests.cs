using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Inventory.Application.Common;
using Inventory.Application.Customers;
using Inventory.Application.Imports;
using Inventory.Application.Products;
using Inventory.Application.Warehouses;
using static Inventory.IntegrationTests.Http;

namespace Inventory.IntegrationTests;

public sealed class ImportTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private static MultipartFormDataContent Csv(string content)
    {
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(content));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        return new MultipartFormDataContent { { file, "file", "data.csv" } };
    }

    private static async Task<ImportResultDto> Import(HttpClient client, string kind, string csv, bool dryRun) =>
        await (await client.PostAsync($"/api/v1/imports/{kind}?dryRun={dryRun}", Csv(csv), Ct)).ReadAsync<ImportResultDto>();

    [Fact]
    public async Task Template_downloads_with_the_expected_columns()
    {
        var (admin, _) = await api.SignInAsync(ApiFactory.AdminEmail);
        var text = await admin.GetStringAsync("/api/v1/imports/products/template", Ct);
        text.ShouldContain("sku,name,description,category_code,base_unit");
    }

    [Fact]
    public async Task Products_dry_run_reports_row_errors_and_saves_nothing()
    {
        var (admin, _) = await api.SignInAsync(ApiFactory.AdminEmail);
        var good = Unique("IMP");
        var csv = $"sku,name,base_unit,min_stock,lot_tracked,has_expiry\n{good},Good item,EA,10,no,no\n{Unique("BAD")},Bad unit,PIECE,,,\n{Unique("BAD2")},Expiry no lot,EA,,no,yes\n";

        var result = await Import(admin, "products", csv, dryRun: false);
        result.Applied.ShouldBeFalse();
        result.Errors.Select(e => e.Row).ShouldBe([3, 4]);
        result.Errors[0].Column.ShouldBe("base_unit");
        (await admin.GetJsonAsync<PagedResult<ProductListItemDto>>($"/api/v1/products?search={good}"))!.TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task Products_import_creates_then_updates_with_packs_and_barcodes()
    {
        var (admin, _) = await api.SignInAsync(ApiFactory.AdminEmail);
        var (sku, ea, ctn) = (Unique("RICE"), Unique("E"), Unique("C"));
        // Semicolon separator and decimal comma, as saved by Excel in French locale.
        var csv = $"sku;name;base_unit;min_stock;lot_tracked;has_expiry;barcode;pack_unit;pack_factor;pack_barcode\n{sku};Rice 1kg;KG;2,5;oui;oui;{ea};CTN;10;{ctn}\n";

        var dry = await Import(admin, "products", csv, dryRun: true);
        dry.Errors.ShouldBeEmpty();
        dry.Applied.ShouldBeFalse();
        dry.Created.ShouldBe(1);

        var applied = await Import(admin, "products", csv, dryRun: false);
        applied.Applied.ShouldBeTrue();
        var lookup = (await admin.GetJsonAsync<BarcodeLookupDto>($"/api/v1/product-barcodes/{ctn}"))!;
        lookup.Sku.ShouldBe(sku);
        lookup.FactorToBase.ShouldBe(10);

        var update = await Import(admin, "products", $"sku,name,base_unit\n{sku},Rice 1 kg bag,KG\n", dryRun: false);
        update.Updated.ShouldBe(1);
        var product = (await admin.GetJsonAsync<ProductDto>($"/api/v1/products/{lookup.ProductId}"))!;
        product.Name.ShouldBe("Rice 1 kg bag");
        product.MinStockQty.ShouldBe(2.5m);   // blank cells keep existing values
        product.Barcodes.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Duplicate_barcode_inside_the_file_is_reported()
    {
        var (admin, _) = await api.SignInAsync(ApiFactory.AdminEmail);
        var code = Unique("DUP");
        var csv = $"sku,name,base_unit,barcode\n{Unique("A")},A,EA,{code}\n{Unique("B")},B,EA,{code}\n";
        var result = await Import(admin, "products", csv, dryRun: true);
        result.Errors.Single().Row.ShouldBe(3);
        result.Errors.Single().Message.ShouldContain("more than once");
    }

    [Fact]
    public async Task Customers_import_with_several_delivery_addresses()
    {
        var (admin, _) = await api.SignInAsync(ApiFactory.AdminEmail);
        var reference = Unique("CUS");
        var csv = $"reference,company_name,email,delivery_label,delivery_line1,delivery_city\n" +
                  $"{reference},Island Foods,info@island.mu,Warehouse,Plaine Lauzun,Port Louis\n" +
                  $"{reference},Island Foods,,Shop,Royal Road,Rose Hill\n";
        var result = await Import(admin, "customers", csv, dryRun: false);
        result.Errors.ShouldBeEmpty();
        result.Created.ShouldBe(1);

        var id = (await admin.GetJsonAsync<PagedResult<CustomerListItemDto>>($"/api/v1/customers?search={reference}"))!.Items.Single().Id;
        var customer = (await admin.GetJsonAsync<CustomerDto>($"/api/v1/customers/{id}"))!;
        customer.Addresses.Count.ShouldBe(2);
        customer.Addresses.Count(a => a.IsDefault).ShouldBe(1);
        customer.Email.ShouldBe("info@island.mu");
    }

    [Fact]
    public async Task Bins_import_respects_warehouse_access()
    {
        var other = await api.CreateWarehouseAsync(Unique("IW")[..10]);
        var otherCode = (await api.WithDbAsync((db, _) => Task.FromResult(db.Warehouses.Single(w => w.Id == other).Code)));
        var managerEmail = await api.CreateUserAsync(RoleCodes.Manager, await api.MainWarehouseIdAsync());
        // Give the manager warehouse management rights for this test via the admin role? No: managers lack warehouses.manage.
        var (manager, _) = await api.SignInAsync(managerEmail);
        (await manager.PostAsync("/api/v1/imports/locations?dryRun=true", Csv("warehouse_code,code\nMAIN,X-1\n"), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var (admin, _) = await api.SignInAsync(ApiFactory.AdminEmail);
        var csv = $"warehouse_code,code,type,zone,pickable\nMAIN,{Unique("Z")[..10]},storage,Z,yes\n{otherCode},Q-1,quarantine,,no\nNOWHERE,A-1,,,\n";
        var result = await Import(admin, "locations", csv, dryRun: false);
        result.Errors.Single().Row.ShouldBe(4);
        result.Errors.Single().Column.ShouldBe("warehouse_code");
        result.Applied.ShouldBeFalse();
    }

    [Fact]
    public async Task Wrong_columns_are_explained()
    {
        var (admin, _) = await api.SignInAsync(ApiFactory.AdminEmail);
        var result = await Import(admin, "products", "code,title\nA,B\n", dryRun: true);
        result.Errors.Single().Message.ShouldContain("Missing column(s): sku, name, base_unit");
    }
}
