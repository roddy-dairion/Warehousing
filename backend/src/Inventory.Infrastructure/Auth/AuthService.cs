using System.Text.Json;
using Inventory.Application.Auth;
using Inventory.Application.Common;
using Inventory.Infrastructure.Identity;
using Inventory.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Inventory.Infrastructure.Auth;

internal sealed class AuthService(
    AppDbContext db,
    UserManager<AppUser> users,
    TokenService tokens,
    ICurrentUser caller,
    IAuditWriter audit,
    TimeProvider clock,
    IOptions<JwtOptions> options,
    ILogger<AuthService> logger) : IAuthService
{
    private const string InvalidCredentials = "Email or password is incorrect.";
    private const string SessionExpired = "Your session has expired. Please sign in again.";

    /// <summary>Two near-simultaneous refreshes (e.g. two browser tabs) within this window are not treated as theft.</summary>
    private static readonly TimeSpan ReuseGracePeriod = TimeSpan.FromSeconds(20);

    private readonly JwtOptions _options = options.Value;

    public async Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is null)
        {
            // Spend comparable time to a real check so response timing does not reveal unknown emails.
            users.PasswordHasher.VerifyHashedPassword(new AppUser(), DummyHash.Value, request.Password);
            await AuditAndSave("auth.login_failed", null, new { reason = "unknown_email" }, cancellationToken);
            throw new AuthenticationFailedException(InvalidCredentials);
        }

        if (!user.IsActive || await users.IsLockedOutAsync(user))
        {
            await AuditAndSave("auth.login_failed", user.Id, new { reason = user.IsActive ? "locked_out" : "inactive" }, cancellationToken);
            throw new AuthenticationFailedException(InvalidCredentials);
        }

        if (!await users.CheckPasswordAsync(user, request.Password))
        {
            await users.AccessFailedAsync(user);
            await AuditAndSave("auth.login_failed", user.Id, new { reason = "bad_password" }, cancellationToken);
            throw new AuthenticationFailedException(InvalidCredentials);
        }

        await users.ResetAccessFailedCountAsync(user);
        user.LastLoginAt = clock.GetUtcNow();
        var result = await IssueAsync(user, request.Client, familyId: Guid.CreateVersion7(), cancellationToken);
        audit.Add("auth.login", "user", user.Id.ToString(), data: new { client = request.Client.ToString().ToLowerInvariant() }, userId: user.Id);
        await db.SaveChangesAsync(cancellationToken);
        return result.Result;
    }

    public async Task<AuthResult> RefreshAsync(string refreshToken, AuthClient client, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            throw new AuthenticationFailedException(SessionExpired);
        }

        var hash = TokenService.Hash(refreshToken);
        var stored = await db.RefreshTokens.AsNoTracking().SingleOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);
        var now = clock.GetUtcNow();

        if (stored is null)
        {
            throw new AuthenticationFailedException(SessionExpired);
        }

        if (stored.RevokedAt is not null)
        {
            await HandleRevokedTokenUseAsync(stored, now, cancellationToken);
            throw new AuthenticationFailedException(SessionExpired);
        }

        if (stored.ExpiresAt <= now)
        {
            throw new AuthenticationFailedException(SessionExpired);
        }

        var user = await users.FindByIdAsync(stored.UserId.ToString());
        if (user is null || !user.IsActive || await users.IsLockedOutAsync(user))
        {
            await RevokeFamilyAsync(stored.FamilyId, now, "user_disabled", cancellationToken);
            throw new AuthenticationFailedException(SessionExpired);
        }

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        // Conditional update: only one concurrent refresh can rotate this token.
        var newTokenId = Guid.CreateVersion7();
        var rotated = await db.RefreshTokens
            .Where(t => t.Id == stored.Id && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.RevokedAt, now)
                .SetProperty(t => t.RevokedReason, "rotated")
                .SetProperty(t => t.ReplacedById, newTokenId), cancellationToken);
        if (rotated != 1)
        {
            await tx.RollbackAsync(cancellationToken);
            throw new AuthenticationFailedException(SessionExpired);
        }

        var issued = await IssueAsync(user, client, stored.FamilyId, cancellationToken, newTokenId);
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return issued.Result;
    }

    public async Task LogoutAsync(string? refreshToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken)) return;
        var hash = TokenService.Hash(refreshToken);
        var token = await db.RefreshTokens.AsNoTracking().SingleOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);
        if (token is null) return;
        await RevokeFamilyAsync(token.FamilyId, clock.GetUtcNow(), "logout", cancellationToken);
        await AuditAndSave("auth.logout", token.UserId, null, cancellationToken);
    }

    public async Task<CurrentUserDto> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null || !user.IsActive) throw new AuthenticationFailedException(SessionExpired);
        var (dto, _, _) = await BuildUserAsync(user, cancellationToken);
        return dto;
    }

    private async Task<(AuthResult Result, RefreshToken Token)> IssueAsync(AppUser user, AuthClient client, Guid familyId, CancellationToken cancellationToken, Guid? tokenId = null)
    {
        var (dto, roles, permissions) = await BuildUserAsync(user, cancellationToken);
        var (accessToken, accessExpires) = tokens.CreateAccessToken(user.Id, user.Email!, user.DisplayName, roles, permissions);

        var raw = TokenService.GenerateRefreshToken();
        var now = clock.GetUtcNow();
        var entity = new RefreshToken
        {
            Id = tokenId ?? Guid.CreateVersion7(),
            UserId = user.Id,
            TokenHash = TokenService.Hash(raw),
            FamilyId = familyId,
            Client = client.ToString().ToLowerInvariant(),
            CreatedAt = now,
            ExpiresAt = now.AddDays(_options.RefreshTokenDays),
            CreatedByIp = caller.IpAddress,
        };
        db.RefreshTokens.Add(entity);
        return (new AuthResult(accessToken, accessExpires, raw, entity.ExpiresAt, dto), entity);
    }

    private async Task<(CurrentUserDto Dto, IList<string> Roles, List<string> Permissions)> BuildUserAsync(AppUser user, CancellationToken cancellationToken)
    {
        var roles = await users.GetRolesAsync(user);
        var permissions = await (
            from ur in db.UserRoles
            join rp in db.RolePermissions on ur.RoleId equals rp.RoleId
            where ur.UserId == user.Id
            select rp.PermissionCode).Distinct().OrderBy(p => p).ToListAsync(cancellationToken);

        var warehousesQuery = db.Warehouses.AsNoTracking().Where(w => w.IsActive);
        if (!user.AllWarehouses)
        {
            warehousesQuery = warehousesQuery.Where(w => db.UserWarehouseAccess.Any(a => a.UserId == user.Id && a.WarehouseId == w.Id));
        }
        var warehouses = await warehousesQuery
            .OrderBy(w => w.Code)
            .Select(w => new WarehouseSummaryDto(w.Id, w.Code, w.Name))
            .ToListAsync(cancellationToken);

        var dto = new CurrentUserDto(user.Id, user.Email!, user.DisplayName, roles.OrderBy(r => r).ToList(), permissions, user.AllWarehouses, warehouses);
        return (dto, roles, permissions);
    }

    private async Task HandleRevokedTokenUseAsync(RefreshToken stored, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var withinGrace = stored.RevokedReason == "rotated" && stored.RevokedAt >= now - ReuseGracePeriod;
        if (withinGrace) return;

        logger.LogWarning("Refresh token reuse detected for user {UserId}, family {FamilyId}; revoking the family", stored.UserId, stored.FamilyId);
        await RevokeFamilyAsync(stored.FamilyId, now, "reuse_detected", cancellationToken);
        await AuditAndSave("auth.refresh_reuse_detected", stored.UserId, new { familyId = stored.FamilyId }, cancellationToken);
    }

    private Task<int> RevokeFamilyAsync(Guid familyId, DateTimeOffset now, string reason, CancellationToken cancellationToken) =>
        db.RefreshTokens
            .Where(t => t.FamilyId == familyId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now).SetProperty(t => t.RevokedReason, reason), cancellationToken);

    private async Task AuditAndSave(string action, Guid? userId, object? data, CancellationToken cancellationToken)
    {
        audit.Add(action, "user", userId?.ToString(), data: data, userId: userId);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static class DummyHash
    {
        public static readonly string Value = new PasswordHasher<AppUser>().HashPassword(new AppUser(), "dummy-password-for-timing");
    }
}

internal static class AuditJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
