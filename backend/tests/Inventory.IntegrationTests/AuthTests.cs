using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Inventory.Application.Auth;
using Microsoft.EntityFrameworkCore;

namespace Inventory.IntegrationTests;

public sealed class AuthTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Health_endpoint_is_public()
    {
        var response = await api.CreateClient().GetAsync("/health", Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Protected_endpoint_without_token_returns_401_problem()
    {
        var response = await api.CreateClient().GetAsync("/api/v1/warehouses", Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task Invalid_login_body_returns_field_errors()
    {
        var response = await api.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new { email = "not-an-email", password = "", client = "web" }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        problem.GetProperty("errors").GetProperty("email").GetArrayLength().ShouldBeGreaterThan(0);
        problem.GetProperty("errors").GetProperty("password").GetArrayLength().ShouldBeGreaterThan(0);
    }

    [Theory]
    [InlineData(ApiFactory.AdminEmail, "wrong-password")]
    [InlineData("nobody@test.local", "Test-Password1")]
    public async Task Wrong_credentials_return_the_same_generic_401(string email, string password)
    {
        var response = await api.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new { email, password, client = "web" }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        problem.GetProperty("detail").GetString().ShouldBe("Email or password is incorrect.");
    }

    [Fact]
    public async Task Web_login_sets_httponly_cookie_and_returns_no_refresh_token_in_body()
    {
        var response = await api.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new { email = ApiFactory.AdminEmail, password = ApiFactory.Password, client = "web" }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("wh_rt=", StringComparison.Ordinal)).ToLowerInvariant();
        cookie.ShouldContain("httponly");
        cookie.ShouldContain("secure");
        cookie.ShouldContain("samesite=strict");
        cookie.ShouldContain("path=/api/v1/auth");

        var body = (await response.Content.ReadFromJsonAsync<AuthResponse>(Json.Options, Ct))!;
        body.RefreshToken.ShouldBeNull();
        body.AccessToken.ShouldNotBeNullOrEmpty();
        body.User.Roles.ShouldBe(["ADMIN"]);
        body.User.AllWarehouses.ShouldBeTrue();
        body.User.Warehouses.Select(w => w.Code).ShouldContain("MAIN");
        body.AccessTokenExpiresAt.ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddMinutes(10));
    }

    [Fact]
    public async Task Mobile_login_returns_refresh_token_in_body_and_sets_no_cookie()
    {
        var response = await api.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new { email = ApiFactory.AdminEmail, password = ApiFactory.Password, client = "mobile" }, Ct);
        response.Headers.Contains("Set-Cookie").ShouldBeFalse();
        var body = (await response.Content.ReadFromJsonAsync<AuthResponse>(Json.Options, Ct))!;
        body.RefreshToken.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task Web_session_survives_refresh_via_cookie_and_ends_on_logout()
    {
        var browser = api.CreateBrowserClient();
        (await browser.PostAsJsonAsync("/api/v1/auth/login", new { email = ApiFactory.AdminEmail, password = ApiFactory.Password, client = "web" }, Ct)).EnsureSuccessStatusCode();

        var refreshed = await browser.PostAsJsonAsync("/api/v1/auth/refresh", new { }, Ct);
        refreshed.StatusCode.ShouldBe(HttpStatusCode.OK);

        (await browser.PostAsJsonAsync("/api/v1/auth/logout", new { }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await browser.PostAsJsonAsync("/api/v1/auth/refresh", new { }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_rotates_the_token_and_reusing_an_old_token_revokes_the_whole_family()
    {
        var email = await api.CreateUserAsync(RoleCodes.Operator, await api.MainWarehouseIdAsync());
        var (_, login) = await api.SignInAsync(email);
        var client = api.CreateClient();

        var first = await Refresh(client, login.RefreshToken!);
        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        var rotated = (await first.Content.ReadFromJsonAsync<AuthResponse>(Json.Options, Ct))!;
        rotated.RefreshToken.ShouldNotBe(login.RefreshToken);

        // Simulate a stolen old token used after the grace period.
        await api.WithDbAsync(async (db, _) =>
        {
            await db.RefreshTokens.Where(t => t.RevokedReason == "rotated")
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, DateTimeOffset.UtcNow.AddMinutes(-5)), Ct);
        });

        (await Refresh(client, login.RefreshToken!)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        // The legitimate (newer) token of the same family is now revoked too.
        (await Refresh(client, rotated.RefreshToken!)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var audited = await api.WithDbAsync((db, _) => db.AuditLogs.AnyAsync(a => a.Action == "auth.refresh_reuse_detected", Ct));
        audited.ShouldBeTrue();
    }

    [Fact]
    public async Task Concurrent_refreshes_with_the_same_token_rotate_only_once()
    {
        var email = await api.CreateUserAsync(RoleCodes.Operator, await api.MainWarehouseIdAsync());
        var (_, login) = await api.SignInAsync(email);

        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Refresh(api.CreateClient(), login.RefreshToken!)));

        results.Count(r => r.StatusCode == HttpStatusCode.OK).ShouldBe(1);
        results.Count(r => r.StatusCode == HttpStatusCode.Unauthorized).ShouldBe(4);
    }

    [Fact]
    public async Task Account_locks_after_repeated_failures_even_with_the_right_password()
    {
        var email = await api.CreateUserAsync(RoleCodes.Operator);
        var client = api.CreateClient();
        for (var i = 0; i < 5; i++)
        {
            (await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "wrong", client = "web" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = ApiFactory.Password, client = "web" }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Deactivated_user_cannot_sign_in_or_refresh()
    {
        var email = await api.CreateUserAsync(RoleCodes.Operator);
        var (_, login) = await api.SignInAsync(email);

        await api.SetUserActiveAsync(email, false);

        (await Refresh(api.CreateClient(), login.RefreshToken!)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var signIn = await api.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new { email, password = ApiFactory.Password, client = "web" }, Ct);
        signIn.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_returns_the_signed_in_user_with_permissions()
    {
        var email = await api.CreateUserAsync(RoleCodes.Manager, await api.MainWarehouseIdAsync());
        var (client, _) = await api.SignInAsync(email);

        var me = (await client.GetFromJsonAsync<CurrentUserDto>("/api/v1/auth/me", Json.Options, Ct))!;
        me.Email.ShouldBe(email);
        me.Permissions.ShouldContain("dispatches.approve");
        me.Permissions.ShouldNotContain("users.manage");
    }

    [Fact]
    public async Task Successful_and_failed_logins_are_audited_without_secrets()
    {
        await api.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new { email = ApiFactory.AdminEmail, password = "Secret-Not-Logged-1", client = "web" }, Ct);
        var entries = await api.WithDbAsync((db, _) => db.AuditLogs.Where(a => a.Action.StartsWith("auth.login")).ToListAsync(Ct));
        entries.ShouldNotBeEmpty();
        entries.ShouldAllBe(e => e.Data == null || !e.Data.Contains("Secret-Not-Logged-1"));
    }

    [Fact]
    public async Task Audit_log_rows_cannot_be_updated_or_deleted()
    {
        await api.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new { email = ApiFactory.AdminEmail, password = "wrong", client = "web" }, Ct);
        var ex = await Should.ThrowAsync<Npgsql.PostgresException>(() => api.WithDbAsync((db, _) =>
            db.Database.ExecuteSqlRawAsync("DELETE FROM audit_logs", Ct)));
        ex.MessageText.ShouldContain("append-only");
    }

    private static Task<HttpResponseMessage> Refresh(HttpClient client, string refreshToken) =>
        client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken }, Ct);
}
