using System.Net;
using Inventory.Application.Reporting;
using static Inventory.IntegrationTests.Http;

namespace Inventory.IntegrationTests;

public sealed class CorsApiFactory : ApiFactory
{
    public const string AllowedOrigin = "https://warehouse.example.test";

    protected override IDictionary<string, string?> ExtraSettings => new Dictionary<string, string?> { ["Cors:AllowedOrigins:0"] = AllowedOrigin };
}

/// <summary>Phase 8: response headers, CORS and report export hardening.</summary>
public sealed class SecurityHardeningTests(CorsApiFactory api) : IClassFixture<CorsApiFactory>
{
    [Theory]
    [InlineData("/health")]
    [InlineData("/api/v1/auth/me")]
    public async Task Every_response_carries_the_defensive_headers(string url)
    {
        var response = await api.CreateClient().GetAsync(url, Ct);
        response.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
        response.Headers.GetValues("X-Frame-Options").ShouldBe(["DENY"]);
        response.Headers.GetValues("Referrer-Policy").ShouldBe(["no-referrer"]);
        response.Headers.GetValues("Content-Security-Policy").Single().ShouldContain("default-src 'none'");
        if (url.StartsWith("/api", StringComparison.Ordinal)) response.Headers.CacheControl!.NoStore.ShouldBeTrue();
    }

    [Fact]
    public async Task Cors_allows_only_the_configured_origins()
    {
        var client = api.CreateClient();
        async Task<HttpResponseMessage> Preflight(string origin)
        {
            var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/auth/login");
            request.Headers.Add("Origin", origin);
            request.Headers.Add("Access-Control-Request-Method", "POST");
            request.Headers.Add("Access-Control-Request-Headers", "content-type");
            return await client.SendAsync(request, Ct);
        }

        var allowed = await Preflight(CorsApiFactory.AllowedOrigin);
        allowed.Headers.GetValues("Access-Control-Allow-Origin").ShouldBe([CorsApiFactory.AllowedOrigin]);
        var other = await Preflight("https://evil.example.test");
        other.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
    }

    [Theory]
    [InlineData("=HYPERLINK(\"http://x\")", "'=HYPERLINK(\"http://x\")")]
    [InlineData("+cmd", "'+cmd")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    [InlineData("-24", "-24")]
    [InlineData("-1.5", "-1.5")]
    [InlineData("- damaged box", "'- damaged box")]
    [InlineData("Soap bar", "Soap bar")]
    [InlineData(null, null)]
    public void Report_csv_cells_cannot_run_as_formulas(string? value, string? expected) =>
        ReportService.SafeCell(value).ShouldBe(expected);

    [Fact]
    public async Task Report_csv_neutralises_formulas_typed_into_product_names()
    {
        var kit = await DispatchKit.CreateAsync(api);
        var units = kit.Stock.Units;
        var p = await (await kit.Admin.PostJsonAsync("/api/v1/products", new { sku = Unique("X"), name = "=1+2", baseUnitId = units["EA"], isLotTracked = false, hasExpiry = false }))
            .ReadAsync<Application.Products.ProductDto>();
        await kit.Stock.ReceiveAsync(InventoryKit.Line(p.Id, 3));
        var csv = await (await kit.Admin.GetAsync($"/api/v1/reports/stock-on-hand/csv?warehouseId={kit.WarehouseId}", Ct)).Content.ReadAsStringAsync(Ct);
        csv.ShouldContain("'=1+2");
        csv.ShouldNotContain(",=1+2");
    }
}
