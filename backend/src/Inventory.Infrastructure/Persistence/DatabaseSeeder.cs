using Inventory.Application.Documents;
using Inventory.Domain.Documents;
using Inventory.Domain.Products;
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

    /// <summary>Company details created when none exist (editable in the admin screens).</summary>
    public string CompanyName { get; set; } = "HelloCherie";
    public string CompanyEmail { get; set; } = "dispatch@hellocherie.ch";
    public string CompanyTimeZone { get; set; } = "Indian/Mauritius";
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
        await EnsureSystemLocationsAsync(cancellationToken);
        await EnsureUnitsAsync(cancellationToken);
        await EnsureAdminAsync(cancellationToken);
        await EnsureDocumentSetupAsync(cancellationToken);
    }

    /// <summary>Company profile and version 1 of each document template (only when missing; edits are never overwritten).</summary>
    private async Task EnsureDocumentSetupAsync(CancellationToken cancellationToken)
    {
        if (!await db.CompanyProfiles.AnyAsync(cancellationToken))
        {
            var company = CompanyProfile.Create(_options.CompanyName);
            company.Update(_options.CompanyName, null, null, null, null, null, _options.CompanyEmail, null, null, null, null, _options.CompanyTimeZone);
            db.CompanyProfiles.Add(company);
        }
        foreach (var type in Enum.GetValues<DocumentType>())
        {
            if (!await db.DocumentTemplates.AnyAsync(t => t.Type == type, cancellationToken))
            {
                db.DocumentTemplates.Add(DocumentTemplate.Create(type, 1, TemplateService.DefaultHtml(type), "Default template", null, clock.GetUtcNow()));
            }
        }
        await db.SaveChangesAsync(cancellationToken);
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

    /// <summary>Every warehouse has RECEIVING, DOCK and QUARANTINE areas.</summary>
    private async Task EnsureSystemLocationsAsync(CancellationToken cancellationToken)
    {
        var warehouses = await db.Warehouses.Select(w => w.Id).ToListAsync(cancellationToken);
        var existing = await db.WarehouseLocations.Where(l => l.IsSystem).Select(l => new { l.WarehouseId, l.Code }).ToListAsync(cancellationToken);
        foreach (var warehouseId in warehouses)
        {
            foreach (var (code, type) in WarehouseLocation.SystemLocations)
            {
                if (!existing.Any(e => e.WarehouseId == warehouseId && e.Code == code))
                {
                    db.WarehouseLocations.Add(WarehouseLocation.CreateSystem(warehouseId, code, type));
                }
            }
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Standard units, created once. Users can rename or add more.</summary>
    private async Task EnsureUnitsAsync(CancellationToken cancellationToken)
    {
        if (await db.UnitsOfMeasure.AnyAsync(cancellationToken)) return;
        (string Code, string Name, int Decimals)[] units =
        [
            ("EA", "Each", 0), ("PK", "Pack", 0), ("BOX", "Box", 0), ("CTN", "Carton", 0), ("PAL", "Pallet", 0),
            ("KG", "Kilogram", 3), ("G", "Gram", 0), ("L", "Litre", 3), ("ML", "Millilitre", 0), ("M", "Metre", 3),
        ];
        foreach (var (code, name, decimals) in units) db.UnitsOfMeasure.Add(UnitOfMeasure.Create(code, name, decimals));
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Created {Count} standard units of measure", units.Length);
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
