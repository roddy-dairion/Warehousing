using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Inventory.Api.Infrastructure;
using Inventory.Domain.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using static Inventory.IntegrationTests.Http;

namespace Inventory.IntegrationTests;

/// <summary>
/// Phase 8: the authorization matrix, checked against every API endpoint (discovered from routing, so new endpoints
/// are covered automatically and an endpoint without a deliberate decision fails the build).
/// </summary>
public sealed partial class EndpointSecurityTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    /// <summary>The only endpoints reachable without signing in.</summary>
    private static readonly string[] Anonymous = ["* /health", "POST api/v1/auth/login", "POST api/v1/auth/logout", "POST api/v1/auth/refresh"];

    /// <summary>
    /// Endpoints open to any signed-in user without a permission attribute. Each one scopes its data itself
    /// (own profile, own warehouses, dashboard sections by permission) or checks the permission in code (imports).
    /// </summary>
    private static readonly string[] SignedInOnly =
    [
        "GET api/v1/auth/me", "GET api/v1/dashboard/warehouse", "GET api/v1/warehouses", "GET api/v1/warehouses/{id:guid}",
        "POST api/v1/imports/{kind}", "GET api/v1/imports/{kind}/template",
    ];

    private List<RouteEndpoint> Endpoints =>
        api.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();

    private static string Key(RouteEndpoint e) => $"{Method(e)} {e.RoutePattern.RawText}";
    private static string Method(RouteEndpoint e) => string.Join(',', e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["*"]);
    private static string[] PermissionsOf(RouteEndpoint e) => e.Metadata.GetOrderedMetadata<HasPermissionAttribute>().SelectMany(a => a.Permissions).ToArray();

    [Fact]
    public void Only_the_sign_in_endpoints_and_health_are_anonymous()
    {
        Endpoints.Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is not null).Select(Key).Order()
            .ToList().ShouldBe(Anonymous.Order().ToList(), "a new anonymous endpoint needs a security review");
    }

    [Fact]
    public void Every_other_endpoint_needs_a_permission_unless_it_is_reviewed_as_signed_in_only()
    {
        Endpoints.Count.ShouldBeGreaterThan(100);
        var withoutPermission = Endpoints
            .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is null && PermissionsOf(e).Length == 0)
            .Select(Key).Order().ToList();
        withoutPermission.ShouldBe(SignedInOnly.Order().ToList(), "an endpoint without [HasPermission] needs a deliberate decision");
        // Every permission named on an endpoint exists in the catalog.
        Endpoints.SelectMany(PermissionsOf).Distinct().ShouldAllBe(p => Permissions.All.ContainsKey(p));
    }

    [Fact]
    public async Task Without_a_token_every_protected_endpoint_answers_401()
    {
        var client = api.CreateClient();
        foreach (var e in Endpoints.Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is null))
        {
            var response = await client.SendAsync(Request(e), Ct);
            response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, Key(e));
        }
    }

    [Theory]
    [InlineData(RoleCodes.Operator)]
    [InlineData(RoleCodes.Manager)]
    [InlineData(RoleCodes.Admin)]
    public async Task Each_role_is_refused_exactly_the_endpoints_it_has_no_permission_for(string role)
    {
        var main = await api.MainWarehouseIdAsync();
        var (client, auth) = await api.SignInAsync(await api.CreateUserAsync(role, main));
        var granted = auth.User.Permissions.ToHashSet();
        var wrong = new List<string>();
        foreach (var e in Endpoints.Where(e => PermissionsOf(e).Length > 0))
        {
            // Several [HasPermission] attributes (controller + action) must all be met; each one needs any of its permissions.
            var allowed = e.Metadata.GetOrderedMetadata<HasPermissionAttribute>().All(a => a.Permissions.Any(granted.Contains));
            var status = (await client.SendAsync(Request(e), Ct)).StatusCode;
            if (allowed ? status is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized : status != HttpStatusCode.Forbidden)
            {
                wrong.Add($"{Key(e)} -> {(int)status} (expected {(allowed ? "not 403" : "403")})");
            }
        }
        wrong.ShouldBeEmpty();

        // Imports check the permission per kind in code.
        var products = (await client.GetAsync("/api/v1/imports/products/template", Ct)).StatusCode;
        products.ShouldBe(granted.Contains(Permissions.ProductsManage) ? HttpStatusCode.OK : HttpStatusCode.Forbidden);
    }

    /// <summary>A request with made-up route values and an empty JSON body. Authorization runs before binding.</summary>
    private static HttpRequestMessage Request(RouteEndpoint e)
    {
        var methods = e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods;
        var method = methods is { Count: > 0 } ? methods[0] : "GET";
        var path = RouteParameter().Replace(e.RoutePattern.RawText!, m => m.Groups[2].Value switch
        {
            ":guid" => Guid.NewGuid().ToString(),
            ":int" => "1",
            _ => m.Groups[1].Value switch
            {
                "kind" => "stock-on-hand",
                "type" => "packingSlip",
                _ => "x",
            },
        });
        var request = new HttpRequestMessage(new HttpMethod(method), "/" + path.TrimStart('/'));
        if (method is "POST" or "PUT")
        {
            var multipart = e.Metadata.GetMetadata<Microsoft.AspNetCore.Http.Metadata.IAcceptsMetadata>()?.ContentTypes.Contains("multipart/form-data") == true;
            request.Content = multipart ? new MultipartFormDataContent() : new StringContent("{}", Encoding.UTF8, "application/json");
        }
        return request;
    }

    [GeneratedRegex(@"\{(\w+)(:\w+)?\}")]
    private static partial Regex RouteParameter();
}
