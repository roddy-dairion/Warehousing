using System.Net.Http.Headers;
using System.Net.Http.Json;
using Inventory.Application.Auth;
using Inventory.Domain.Security;
using Inventory.Domain.Warehouses;
using Inventory.Infrastructure.Identity;
using Inventory.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Inventory.IntegrationTests;

/// <summary>
/// Runs the real API against a throw-away PostgreSQL database (created per test class, dropped afterwards).
/// Server: env INVENTORY_TEST_DB (default: localhost, postgres/postgres). Start one with
/// <c>docker compose -f deploy/docker-compose.dev.yml up -d</c>.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string AdminEmail = "admin@test.local";
    public const string Password = "Test-Password1";

    private static readonly string ServerConnection =
        Environment.GetEnvironmentVariable("INVENTORY_TEST_DB") ?? "Host=localhost;Port=55432;Username=postgres;Password=postgres";

    private readonly string _database = $"inventory_test_{Guid.NewGuid():N}";

    public string ConnectionString => new NpgsqlConnectionStringBuilder(ServerConnection) { Database = _database }.ConnectionString;

    /// <summary>Overridable settings (e.g. lower rate limits for a specific test class).</summary>
    protected virtual IDictionary<string, string?> ExtraSettings => new Dictionary<string, string?>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", ConnectionString);
        builder.UseSetting("Jwt:SigningKey", "integration-test-signing-key-0123456789abcdef");
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Seed:AdminEmail", AdminEmail);
        builder.UseSetting("Seed:AdminPassword", Password);
        builder.UseSetting("RateLimiting:LoginPerMinute", "1000");
        builder.UseSetting("RateLimiting:RefreshPerMinute", "1000");
        builder.UseSetting("Serilog:MinimumLevel:Default", "Warning");
        foreach (var (key, value) in ExtraSettings) builder.UseSetting(key, value);
    }

    public async ValueTask InitializeAsync()
    {
        // Starting the server applies migrations and seeds roles, the MAIN warehouse and the admin.
        _ = Server;
        await Task.CompletedTask;
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        NpgsqlConnection.ClearAllPools();
        await using var conn = new NpgsqlConnection(ServerConnection);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_database}\" WITH (FORCE)", conn);
        await cmd.ExecuteNonQueryAsync();
        GC.SuppressFinalize(this);
    }

    /// <summary>Client that does NOT follow redirects and keeps cookies (like a browser).</summary>
    public HttpClient CreateBrowserClient() => CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, BaseAddress = new Uri("https://localhost") });

    public async Task<T> WithDbAsync<T>(Func<AppDbContext, IServiceProvider, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>(), scope.ServiceProvider);
    }

    public Task WithDbAsync(Func<AppDbContext, IServiceProvider, Task> action) =>
        WithDbAsync<object?>(async (db, sp) => { await action(db, sp); return null; });

    public Task<Guid> CreateWarehouseAsync(string code, bool active = true) => WithDbAsync(async (db, _) =>
    {
        var w = Warehouse.Create(code, $"{code} warehouse");
        if (!active) w.Deactivate();
        db.Warehouses.Add(w);
        foreach (var (binCode, type) in WarehouseLocation.SystemLocations) db.WarehouseLocations.Add(WarehouseLocation.CreateSystem(w.Id, binCode, type));
        await db.SaveChangesAsync();
        return w.Id;
    });

    public Task<Guid> MainWarehouseIdAsync() => WithDbAsync((db, _) => db.Warehouses.Where(w => w.Code == "MAIN").Select(w => w.Id).SingleAsync());

    /// <summary>Creates a user with one role and access to the given warehouses.</summary>
    public Task<string> CreateUserAsync(string role, params Guid[] warehouseIds) => WithDbAsync(async (db, sp) =>
    {
        var users = sp.GetRequiredService<UserManager<AppUser>>();
        var email = $"{role.ToLowerInvariant()}-{Guid.NewGuid():N}@test.local";
        var user = new AppUser { UserName = email, Email = email, DisplayName = $"Test {role}", CreatedAt = DateTimeOffset.UtcNow };
        (await users.CreateAsync(user, Password)).Succeeded.ShouldBeTrue();
        (await users.AddToRoleAsync(user, role)).Succeeded.ShouldBeTrue();
        foreach (var id in warehouseIds)
        {
            db.UserWarehouseAccess.Add(new UserWarehouseAccess { UserId = user.Id, WarehouseId = id, GrantedAt = DateTimeOffset.UtcNow });
        }
        await db.SaveChangesAsync();
        return email;
    });

    public Task SetUserActiveAsync(string email, bool active) => WithDbAsync(async (db, _) =>
    {
        var user = await db.Users.SingleAsync(u => u.Email == email);
        user.IsActive = active;
        await db.SaveChangesAsync();
    });

    /// <summary>Signs in as a mobile client and returns an HttpClient with the bearer token, plus the auth response.</summary>
    public async Task<(HttpClient Client, AuthResponse Auth)> SignInAsync(string email, string? password = null)
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = password ?? Password, client = "mobile" });
        response.EnsureSuccessStatusCode();
        var auth = (await response.Content.ReadFromJsonAsync<AuthResponse>(Json.Options))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return (client, auth);
    }
}

internal static class Json
{
    public static readonly System.Text.Json.JsonSerializerOptions Options = new(System.Text.Json.JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase) },
    };
}

internal static class RoleCodes
{
    public const string Admin = Roles.Administrator;
    public const string Manager = Roles.WarehouseManager;
    public const string Operator = Roles.WarehouseOperator;
}
