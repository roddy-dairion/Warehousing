using System.Net;
using System.Net.Http.Json;
using Inventory.Application.Common;
using Inventory.Application.Users;
using static Inventory.IntegrationTests.Http;

namespace Inventory.IntegrationTests;

public sealed class UserAdminTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private static string Email() => $"user-{Guid.NewGuid():N}@test.local";

    [Fact]
    public async Task Admin_creates_a_user_who_can_then_sign_in_with_the_granted_access()
    {
        var (admin, _) = await api.SignInAsync(ApiFactory.AdminEmail);
        var main = await api.MainWarehouseIdAsync();
        var email = Email();

        var user = await (await admin.PostJsonAsync("/api/v1/users", new
        {
            email, displayName = "New Picker", password = "Picker-Pass-1", roles = new[] { "WH_OPERATOR" }, allWarehouses = false, warehouseIds = new[] { main },
        })).ReadAsync<UserDto>();
        user.Roles.ShouldBe(["WH_OPERATOR"]);
        user.Warehouses.Single().Code.ShouldBe("MAIN");

        var (_, auth) = await api.SignInAsync(email, "Picker-Pass-1");
        auth.User.Permissions.ShouldContain("dispatches.pick");
        auth.User.Warehouses.Single().Code.ShouldBe("MAIN");
    }

    [Fact]
    public async Task Weak_password_and_duplicate_email_are_rejected()
    {
        var (admin, _) = await api.SignInAsync(ApiFactory.AdminEmail);
        var weak = await admin.PostJsonAsync("/api/v1/users", new { email = Email(), displayName = "X", password = "alllowercase", roles = new[] { "WH_OPERATOR" } });
        (await weak.ProblemAsync(HttpStatusCode.UnprocessableEntity)).Code().ShouldBe("user.identity");

        var dup = await admin.PostJsonAsync("/api/v1/users", new { email = ApiFactory.AdminEmail, displayName = "X", password = "Strong-Pass-1", roles = new[] { "WH_OPERATOR" } });
        (await dup.ProblemAsync(HttpStatusCode.Conflict)).Code().ShouldBe("user.email_taken");
    }

    [Fact]
    public async Task Non_admins_cannot_manage_users()
    {
        var (manager, _) = await api.SignInAsync(await api.CreateUserAsync(RoleCodes.Manager, await api.MainWarehouseIdAsync()));
        (await manager.GetAsync("/api/v1/users", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await manager.PostJsonAsync("/api/v1/users", new { email = Email(), displayName = "X", password = "Strong-Pass-1", roles = new[] { "ADMIN" } }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Admins_cannot_lock_themselves_out()
    {
        var (admin, auth) = await api.SignInAsync(ApiFactory.AdminEmail);
        (await (await admin.PostAsync($"/api/v1/users/{auth.User.Id}/deactivate")).ProblemAsync(HttpStatusCode.Conflict)).Code().ShouldBe("user.self_deactivate");

        var me = (await admin.GetJsonAsync<UserDto>($"/api/v1/users/{auth.User.Id}"))!;
        var demote = await admin.PutJsonAsync($"/api/v1/users/{me.Id}", new { displayName = me.DisplayName, roles = new[] { "WH_MANAGER" }, allWarehouses = true, concurrencyStamp = me.ConcurrencyStamp });
        (await demote.ProblemAsync(HttpStatusCode.Conflict)).Code().ShouldBe("user.self_demote");
    }

    [Fact]
    public async Task Deactivating_a_user_ends_their_sessions()
    {
        var (admin, _) = await api.SignInAsync(ApiFactory.AdminEmail);
        var email = await api.CreateUserAsync(RoleCodes.Operator, await api.MainWarehouseIdAsync());
        var (_, login) = await api.SignInAsync(email);
        var id = login.User.Id;

        (await admin.PostAsync($"/api/v1/users/{id}/deactivate")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await api.CreateClient().PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = login.RefreshToken }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var list = (await admin.GetJsonAsync<PagedResult<UserListItemDto>>($"/api/v1/users?search={email}"))!;
        list.Items.Single().IsActive.ShouldBeFalse();
    }

    [Fact]
    public async Task Password_reset_replaces_the_password_and_unlocks()
    {
        var (admin, _) = await api.SignInAsync(ApiFactory.AdminEmail);
        var email = await api.CreateUserAsync(RoleCodes.Operator);
        var client = api.CreateClient();
        for (var i = 0; i < 5; i++) await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "wrong", client = "web" }, Ct);
        var id = (await admin.GetJsonAsync<PagedResult<UserListItemDto>>($"/api/v1/users?search={email}"))!.Items.Single().Id;
        (await admin.GetJsonAsync<UserDto>($"/api/v1/users/{id}"))!.IsLockedOut.ShouldBeTrue();

        (await admin.PostJsonAsync($"/api/v1/users/{id}/reset-password", new { newPassword = "Brand-New-Pass-2" })).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = ApiFactory.Password, client = "web" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "Brand-New-Pass-2", client = "web" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Changing_roles_revokes_sessions_and_new_tokens_carry_new_permissions()
    {
        var (admin, _) = await api.SignInAsync(ApiFactory.AdminEmail);
        var main = await api.MainWarehouseIdAsync();
        var email = await api.CreateUserAsync(RoleCodes.Operator, main);
        var (_, login) = await api.SignInAsync(email);
        var user = (await admin.GetJsonAsync<UserDto>($"/api/v1/users/{login.User.Id}"))!;

        (await admin.PutJsonAsync($"/api/v1/users/{user.Id}", new
        {
            displayName = user.DisplayName, roles = new[] { "WH_MANAGER" }, allWarehouses = false, warehouseIds = new[] { main }, concurrencyStamp = user.ConcurrencyStamp,
        })).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await api.CreateClient().PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = login.RefreshToken }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var (_, again) = await api.SignInAsync(email);
        again.User.Permissions.ShouldContain("dispatches.approve");
    }
}
