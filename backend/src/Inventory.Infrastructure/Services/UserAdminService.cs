using System.Linq.Expressions;
using Inventory.Application.Auth;
using Inventory.Application.Common;
using Inventory.Application.Users;
using Inventory.Domain.Common;
using Inventory.Domain.Security;
using Inventory.Infrastructure.Identity;
using Inventory.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Infrastructure.Services;

/// <summary>
/// User administration. Safety rules: users cannot deactivate or demote themselves, and at least one
/// active administrator with access to all warehouses must always remain.
/// Deactivation, password resets and role/access changes revoke the user's sessions.
/// </summary>
internal sealed class UserAdminService(
    AppDbContext db,
    UserManager<AppUser> users,
    ICurrentUser caller,
    IAuditWriter audit,
    TimeProvider clock) : IUserAdminService
{
    private static readonly Dictionary<string, Expression<Func<AppUser, object?>>> Sorts = new()
    {
        ["email"] = u => u.Email,
        ["displayName"] = u => u.DisplayName,
        ["lastLoginAt"] = u => u.LastLoginAt,
        ["isActive"] = u => u.IsActive,
    };

    public async Task<PagedResult<UserListItemDto>> ListAsync(UserListQuery query, CancellationToken cancellationToken)
    {
        var q = db.Users.AsNoTracking();
        if (query.Term is { } term) q = q.Where(u => u.Email!.ToLower().Contains(term) || u.DisplayName.ToLower().Contains(term));
        if (query.IsActive is { } active) q = q.Where(u => u.IsActive == active);
        if (!string.IsNullOrWhiteSpace(query.Role))
        {
            var role = query.Role.Trim().ToUpperInvariant();
            q = q.Where(u => db.UserRoles.Any(ur => ur.UserId == u.Id && db.Roles.Any(r => r.Id == ur.RoleId && r.NormalizedName == role)));
        }

        var now = clock.GetUtcNow();
        var page = await q.SortBy(query, Sorts, "displayName", u => u.Id)
            .Select(u => new
            {
                u.Id, u.Email, u.DisplayName, u.AllWarehouses, u.IsActive, u.LastLoginAt,
                Locked = u.LockoutEnd != null && u.LockoutEnd > now,
                Roles = db.Roles.Where(r => db.UserRoles.Any(ur => ur.UserId == u.Id && ur.RoleId == r.Id)).Select(r => r.Name!).ToList(),
                Warehouses = db.Warehouses.Where(w => db.UserWarehouseAccess.Any(a => a.UserId == u.Id && a.WarehouseId == w.Id)).Select(w => w.Code).ToList(),
            })
            .ToPagedAsync(query, cancellationToken);

        return new PagedResult<UserListItemDto>(
            page.Items.Select(u => new UserListItemDto(u.Id, u.Email!, u.DisplayName, u.Roles.Order().ToList(), u.AllWarehouses,
                u.Warehouses.Order().ToList(), u.IsActive, u.Locked, u.LastLoginAt)).ToList(),
            page.Page, page.PageSize, page.TotalCount);
    }

    public async Task<UserDto> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id, cancellationToken) ?? throw new NotFoundException("User not found.");
        var roles = await db.Roles.Where(r => db.UserRoles.Any(ur => ur.UserId == id && ur.RoleId == r.Id)).Select(r => r.Name!).OrderBy(n => n).ToListAsync(cancellationToken);
        var warehouses = await db.Warehouses.Where(w => db.UserWarehouseAccess.Any(a => a.UserId == id && a.WarehouseId == w.Id))
            .OrderBy(w => w.Code).Select(w => new WarehouseSummaryDto(w.Id, w.Code, w.Name)).ToListAsync(cancellationToken);
        var locked = user.LockoutEnd is { } end && end > clock.GetUtcNow();
        return new UserDto(user.Id, user.Email!, user.DisplayName, roles, user.AllWarehouses, warehouses, user.IsActive, locked, user.CreatedAt, user.LastLoginAt, user.ConcurrencyStamp!);
    }

    public async Task<UserDto> CreateAsync(CreateUserRequest r, CancellationToken cancellationToken)
    {
        var roles = await ValidRolesAsync(r.Roles, cancellationToken);
        var warehouseIds = await ValidWarehousesAsync(r.WarehouseIds, cancellationToken);
        var email = r.Email.Trim();
        if (await users.FindByEmailAsync(email) is not null)
        {
            throw new DomainRuleException("user.email_taken", $"A user with email {email} already exists.", isConflict: true);
        }

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var user = new AppUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = r.DisplayName.Trim(),
            AllWarehouses = r.AllWarehouses,
            CreatedAt = clock.GetUtcNow(),
        };
        Check(await users.CreateAsync(user, r.Password));
        Check(await users.AddToRolesAsync(user, roles));
        SetWarehouses(user.Id, r.AllWarehouses ? [] : warehouseIds);
        audit.Add("user.created", "user", user.Id.ToString(), data: new { user.Email, roles, user.AllWarehouses, warehouses = warehouseIds });
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return await GetAsync(user.Id, cancellationToken);
    }

    public async Task<UserDto> UpdateAsync(Guid id, UpdateUserRequest r, CancellationToken cancellationToken)
    {
        var user = await LoadAsync(id);
        if (user.ConcurrencyStamp != r.ConcurrencyStamp)
        {
            throw new DbUpdateConcurrencyException("This user was changed by someone else. Reload and try again.");
        }
        var roles = await ValidRolesAsync(r.Roles, cancellationToken);
        var warehouseIds = await ValidWarehousesAsync(r.WarehouseIds, cancellationToken);
        var currentRoles = await users.GetRolesAsync(user);

        if (id == caller.Id && (currentRoles.Contains(Roles.Administrator) && !roles.Contains(Roles.Administrator) || (user.AllWarehouses && !r.AllWarehouses)))
        {
            throw new DomainRuleException("user.self_demote", "You cannot remove your own administrator role or all-warehouse access.", isConflict: true);
        }

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        user.DisplayName = r.DisplayName.Trim();
        var accessChanged = user.AllWarehouses != r.AllWarehouses;
        user.AllWarehouses = r.AllWarehouses;
        Check(await users.UpdateAsync(user));

        var toRemove = currentRoles.Except(roles).ToList();
        var toAdd = roles.Except(currentRoles).ToList();
        if (toRemove.Count > 0) Check(await users.RemoveFromRolesAsync(user, toRemove));
        if (toAdd.Count > 0) Check(await users.AddToRolesAsync(user, toAdd));

        var existing = await db.UserWarehouseAccess.Where(a => a.UserId == id).Select(a => a.WarehouseId).ToListAsync(cancellationToken);
        var target = r.AllWarehouses ? [] : warehouseIds;
        if (!existing.ToHashSet().SetEquals(target))
        {
            accessChanged = true;
            await db.UserWarehouseAccess.Where(a => a.UserId == id).ExecuteDeleteAsync(cancellationToken);
            SetWarehouses(id, target);
        }

        await EnsureAdminRemainsAsync(cancellationToken);
        if (toRemove.Count > 0 || toAdd.Count > 0 || accessChanged) await RevokeSessionsAsync(id, "access_changed", cancellationToken);
        audit.Add("user.updated", "user", id.ToString(), data: new { user.DisplayName, rolesBefore = currentRoles, rolesAfter = roles, user.AllWarehouses, warehouses = target });
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task SetActiveAsync(Guid id, bool active, CancellationToken cancellationToken)
    {
        if (id == caller.Id && !active)
        {
            throw new DomainRuleException("user.self_deactivate", "You cannot deactivate your own account.", isConflict: true);
        }
        var user = await LoadAsync(id);
        if (user.IsActive == active) return;

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        user.IsActive = active;
        Check(await users.UpdateAsync(user));
        if (!active)
        {
            await EnsureAdminRemainsAsync(cancellationToken);
            await RevokeSessionsAsync(id, "user_deactivated", cancellationToken);
        }
        audit.Add(active ? "user.activated" : "user.deactivated", "user", id.ToString(), data: new { user.Email });
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    public async Task ResetPasswordAsync(Guid id, ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        var user = await LoadAsync(id);
        foreach (var validator in users.PasswordValidators)
        {
            Check(await validator.ValidateAsync(users, user, request.NewPassword));
        }
        user.PasswordHash = users.PasswordHasher.HashPassword(user, request.NewPassword);
        Check(await users.UpdateSecurityStampAsync(user));
        await users.SetLockoutEndDateAsync(user, null);
        await users.ResetAccessFailedCountAsync(user);
        await RevokeSessionsAsync(id, "password_reset", cancellationToken);
        audit.Add("user.password_reset", "user", id.ToString(), data: new { user.Email });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UnlockAsync(Guid id, CancellationToken cancellationToken)
    {
        var user = await LoadAsync(id);
        Check(await users.SetLockoutEndDateAsync(user, null));
        Check(await users.ResetAccessFailedCountAsync(user));
        audit.Add("user.unlocked", "user", id.ToString(), data: new { user.Email });
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<AppUser> LoadAsync(Guid id) => await users.FindByIdAsync(id.ToString()) ?? throw new NotFoundException("User not found.");

    private async Task<List<string>> ValidRolesAsync(IReadOnlyList<string> requested, CancellationToken cancellationToken)
    {
        var normalized = requested.Select(r => r.Trim().ToUpperInvariant()).Distinct().ToList();
        var known = await db.Roles.Where(r => normalized.Contains(r.NormalizedName!)).Select(r => r.Name!).ToListAsync(cancellationToken);
        var unknown = normalized.Except(known, StringComparer.OrdinalIgnoreCase).ToList();
        if (unknown.Count > 0) throw new DomainRuleException("user.role_unknown", $"Unknown role(s): {string.Join(", ", unknown)}.");
        return known;
    }

    private async Task<List<Guid>> ValidWarehousesAsync(IReadOnlyList<Guid>? requested, CancellationToken cancellationToken)
    {
        var ids = (requested ?? []).Distinct().ToList();
        var found = await db.Warehouses.Where(w => ids.Contains(w.Id)).Select(w => w.Id).ToListAsync(cancellationToken);
        if (found.Count != ids.Count) throw new DomainRuleException("user.warehouse_unknown", "One or more warehouses do not exist.");
        return found;
    }

    private void SetWarehouses(Guid userId, IEnumerable<Guid> warehouseIds)
    {
        var now = clock.GetUtcNow();
        foreach (var w in warehouseIds)
        {
            db.UserWarehouseAccess.Add(new UserWarehouseAccess { UserId = userId, WarehouseId = w, GrantedAt = now, GrantedBy = caller.IdOrNull });
        }
    }

    /// <summary>Must run inside the transaction, after the change is applied.</summary>
    private async Task EnsureAdminRemainsAsync(CancellationToken cancellationToken)
    {
        await db.SaveChangesAsync(cancellationToken);
        var admins = await db.Users.CountAsync(u => u.IsActive && u.AllWarehouses &&
            db.UserRoles.Any(ur => ur.UserId == u.Id && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == Roles.Administrator)), cancellationToken);
        if (admins == 0)
        {
            throw new DomainRuleException("user.last_admin", "At least one active administrator with access to all warehouses must remain.", isConflict: true);
        }
    }

    private Task<int> RevokeSessionsAsync(Guid userId, string reason, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        return db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now).SetProperty(t => t.RevokedReason, reason), cancellationToken);
    }

    private static void Check(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            var message = string.Join(" ", result.Errors.Select(e => e.Description));
            throw new DomainRuleException("user.identity", message);
        }
    }
}
