using System.Net;
using System.Net.Http.Json;
using Inventory.Application.Warehouses;

namespace Inventory.IntegrationTests;

public sealed class AuthorizationTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Operator_is_forbidden_from_user_management_endpoints()
    {
        var email = await api.CreateUserAsync(RoleCodes.Operator, await api.MainWarehouseIdAsync());
        var (client, _) = await api.SignInAsync(email);

        (await client.GetAsync("/api/v1/roles", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await client.GetAsync("/api/v1/permissions", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Admin_can_list_roles_and_permissions()
    {
        var (client, _) = await api.SignInAsync(ApiFactory.AdminEmail);
        (await client.GetAsync("/api/v1/roles", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var permissions = await client.GetFromJsonAsync<List<object>>("/api/v1/permissions", Ct);
        permissions!.Count.ShouldBe(Inventory.Domain.Security.Permissions.All.Count);
    }

    [Fact]
    public async Task Tampered_token_is_rejected()
    {
        var (client, auth) = await api.SignInAsync(ApiFactory.AdminEmail);
        var parts = auth.AccessToken.Split('.');
        var tampered = $"{parts[0]}.{parts[1]}x.{parts[2]}";
        client.DefaultRequestHeaders.Authorization = new("Bearer", tampered);
        (await client.GetAsync("/api/v1/warehouses", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Staff_only_see_warehouses_they_were_granted()
    {
        var main = await api.MainWarehouseIdAsync();
        var north = await api.CreateWarehouseAsync("NORTH");
        var email = await api.CreateUserAsync(RoleCodes.Operator, main);
        var (client, auth) = await api.SignInAsync(email);

        auth.User.Warehouses.Select(w => w.Code).ShouldBe(["MAIN"]);
        var list = await client.GetFromJsonAsync<List<WarehouseDto>>("/api/v1/warehouses", Ct);
        list!.Select(w => w.Code).ShouldBe(["MAIN"]);

        // Existence of other warehouses is not leaked: 404, not 403.
        (await client.GetAsync($"/api/v1/warehouses/{north}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.GetAsync($"/api/v1/warehouses/{main}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Administrators_see_every_active_warehouse_but_not_inactive_ones()
    {
        await api.CreateWarehouseAsync("EAST");
        var closed = await api.CreateWarehouseAsync("CLOSED", active: false);
        var (client, _) = await api.SignInAsync(ApiFactory.AdminEmail);

        var codes = (await client.GetFromJsonAsync<List<WarehouseDto>>("/api/v1/warehouses", Ct))!.Select(w => w.Code).ToList();
        codes.ShouldContain("MAIN");
        codes.ShouldContain("EAST");
        codes.ShouldNotContain("CLOSED");
        (await client.GetAsync($"/api/v1/warehouses/{closed}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Adding_a_warehouse_does_not_change_existing_users_access()
    {
        var main = await api.MainWarehouseIdAsync();
        var email = await api.CreateUserAsync(RoleCodes.Operator, main);

        await api.CreateWarehouseAsync("SOUTH");

        var (_, auth) = await api.SignInAsync(email);
        auth.User.Warehouses.Select(w => w.Code).ShouldBe(["MAIN"]);
    }
}
