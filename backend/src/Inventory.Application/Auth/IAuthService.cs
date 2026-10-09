namespace Inventory.Application.Auth;

public interface IAuthService
{
    /// <exception cref="Common.AuthenticationFailedException">Wrong credentials, locked or inactive account.</exception>
    Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken);

    /// <summary>Rotates the refresh token. Reusing a rotated token revokes the whole token family.</summary>
    Task<AuthResult> RefreshAsync(string refreshToken, AuthClient client, CancellationToken cancellationToken);

    /// <summary>Revokes the refresh token. Unknown tokens are ignored (idempotent).</summary>
    Task LogoutAsync(string? refreshToken, CancellationToken cancellationToken);

    Task<CurrentUserDto> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken);
}
