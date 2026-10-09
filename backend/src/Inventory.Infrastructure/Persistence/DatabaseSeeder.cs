using Inventory.Domain.Security;
using Inventory.Domain.Warehouses;
using Inventory.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Inventory.Infrastructure.Persistence;

public sealed class SeedOptions
{
    public const string Section = "Seed";

    /// <summary>Code and name of the warehouse created when the database has none.</summary>
    public string WarehouseCode { get; set; } = "MAIN";
    public string WarehouseName { get; set; } = "Main warehouse";

    /// <summary>First administrator, created only when there are no users. Supply via secrets/environment, never in appsettings.</summary>
    public string? AdminEmail { get; set; }
    public string? AdminPassword { get; set; }
    public string AdminDisplayName { get; set; } = "Administrator";
}

/// <summary>Idempotent start-up seeding: permission catalogue, built-in roles, first warehouse, first admin.</summary>
public sealed class DatabaseSeeder(
    AppDbContext db,
    UserManager<AppUser> users,
    RoleManager<AppRole> roles,
    TimeProvider clock,
    IOptions<SeedOptions> options,
    ILogger<DatabaseSeeder> logger)
{
    private readonly SeedOptions _options = options.Value;

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        await SyncPermissionsAsync(cancellationToken);
        await EnsureRolesAsync(cancellationToken);
        await EnsureWarehouseAsync(cancellationToken);
        await EnsureAdminAsync(cancellationToken);
    }

    private async Task SyncPermissionsAsync(CancellationToken cancellationToken)
    {
        var existing = await db.Permissions.ToDictionaryAsync(p => p.Code, cancellationToken);
        foreach (var (code, description) in Permissions.All)
        {
            if (existing.TryGetValue(code, out var p))
            {
                p.Description = description;
            }
            else
            {
                db.Permissions.Add(new Permission { Code = code, Description = description });
            }
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Creates missing built-in roles with their default permissions. Existing roles are left as configured.</summary>
    private async Task EnsureRolesAsync(CancellationToken cancellationToken)
    {
        foreach (var definition in Roles.Defaults)
        {
            if (await roles.RoleExistsAsync(definition.Code)) continue;
            var role = new AppRole { Name = definition.Code, DisplayName = definition.Name };
            Check(await roles.CreateAsync(role), $"create role {definition.Code}");
            db.RolePermissions.AddRange(definition.Permissions.Select(p => new RolePermission { RoleId = role.Id, PermissionCode = p }));
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Created role {Role}", definition.Code);
        }
    }

    private async Task EnsureWarehouseAsync(CancellationToken cancellationToken)
    {
        if (await db.Warehouses.AnyAsync(cancellationToken)) return;
        db.Warehouses.Add(Warehouse.Create(_options.WarehouseCode, _options.WarehouseName));
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Created warehouse {Code}", _options.WarehouseCode);
    }

    private async Task EnsureAdminAsync(CancellationToken cancellationToken)
    {
        if (await db.Users.AnyAsync(cancellationToken)) return;
        if (string.IsNullOrWhiteSpace(_options.AdminEmail) || string.IsNullOrWhiteSpace(_options.AdminPassword))
        {
            logger.LogWarning("No users exist and Seed:AdminEmail / Seed:AdminPassword are not set; no administrator was created");
            return;
        }

        var admin = new AppUser
        {
            UserName = _options.AdminEmail,
            Email = _options.AdminEmail,
            EmailConfirmed = true,
            DisplayName = _options.AdminDisplayName,
            AllWarehouses = true,
            CreatedAt = clock.GetUtcNow(),
        };
        Check(await users.CreateAsync(admin, _options.AdminPassword), "create administrator");
        Check(await users.AddToRoleAsync(admin, Roles.Administrator), "assign administrator role");
        logger.LogInformation("Created administrator {Email}", _options.AdminEmail);
    }

    private static void Check(IdentityResult result, string what)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"Seeding failed to {what}: {string.Join("; ", result.Errors.Select(e => e.Description))}");
        }
    }
}
