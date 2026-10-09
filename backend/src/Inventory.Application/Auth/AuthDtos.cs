namespace Inventory.Application.Auth;

public enum AuthClient
{
    Web,
    Mobile,
}

public sealed record LoginRequest(string Email, string Password, AuthClient Client);

/// <summary>Body of POST /auth/refresh and /auth/logout. The web client sends the token as a cookie instead.</summary>
public sealed record RefreshRequest(string? RefreshToken);

public sealed record WarehouseSummaryDto(Guid Id, string Code, string Name);

public sealed record CurrentUserDto(
    Guid Id,
    string Email,
    string DisplayName,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions,
    bool AllWarehouses,
    IReadOnlyList<WarehouseSummaryDto> Warehouses);

/// <summary>Result of a successful login or refresh. <see cref="RefreshToken"/> is the raw token (never stored).</summary>
public sealed record AuthResult(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    CurrentUserDto User);

/// <summary>Response body. <c>RefreshToken</c> is only filled for the mobile client.</summary>
public sealed record AuthResponse(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string? RefreshToken, CurrentUserDto User);
