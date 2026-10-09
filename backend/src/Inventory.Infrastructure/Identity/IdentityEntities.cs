using Microsoft.AspNetCore.Identity;

namespace Inventory.Infrastructure.Identity;

/// <summary>An internal staff user.</summary>
public sealed class AppUser : IdentityUser<Guid>
{
    public AppUser()
    {
        Id = Guid.CreateVersion7();
        SecurityStamp = Guid.NewGuid().ToString();
    }

    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Deactivated users cannot sign in or refresh. Users are never deleted.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>May act in every active warehouse (administrators). Otherwise see <see cref="UserWarehouseAccess"/>.</summary>
    public bool AllWarehouses { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
}

public sealed class AppRole : IdentityRole<Guid>
{
    public AppRole()
    {
        Id = Guid.CreateVersion7();
    }

    /// <summary>Human-readable name; <see cref="IdentityRole{TKey}.Name"/> holds the stable code (e.g. WH_MANAGER).</summary>
    public string DisplayName { get; set; } = string.Empty;
}

public sealed class Permission
{
    public string Code { get; set; } = null!;
    public string Description { get; set; } = null!;
}

public sealed class RolePermission
{
    public Guid RoleId { get; set; }
    public string PermissionCode { get; set; } = null!;
}

public sealed class UserWarehouseAccess
{
    public Guid UserId { get; set; }
    public Guid WarehouseId { get; set; }
    public DateTimeOffset GrantedAt { get; set; }
    public Guid? GrantedBy { get; set; }
}

/// <summary>A refresh token. Only the SHA-256 hash is stored.</summary>
public sealed class RefreshToken
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = null!;
    /// <summary>All tokens descended from one login share a family; reuse of a rotated token revokes the family.</summary>
    public Guid FamilyId { get; set; }
    public string Client { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public string? CreatedByIp { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? RevokedReason { get; set; }
    public Guid? ReplacedById { get; set; }
}
