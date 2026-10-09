using Inventory.Api.Infrastructure;
using Inventory.Application.Auth;
using Inventory.Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Inventory.Api.Controllers;

/// <summary>Sign-in, token refresh and sign-out. See docs/05-auth-api-contract.md.</summary>
[ApiController]
[Route("api/v1/auth")]
[Produces("application/json")]
public sealed class AuthController(IAuthService auth, ICurrentUser currentUser) : ControllerBase
{
    /// <summary>Signs in with email and password.</summary>
    /// <remarks>Web clients receive the refresh token as an HttpOnly cookie; mobile clients receive it in the body.</remarks>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<AuthResponse> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var result = await auth.LoginAsync(request, cancellationToken);
        return ToResponse(result, request.Client);
    }

    /// <summary>Exchanges a refresh token for a new access token and a rotated refresh token.</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Refresh)]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<AuthResponse> Refresh(RefreshRequest? request, CancellationToken cancellationToken)
    {
        var (token, client) = ResolveRefreshToken(request);
        try
        {
            var result = await auth.RefreshAsync(token ?? string.Empty, client, cancellationToken);
            return ToResponse(result, client);
        }
        catch (AuthenticationFailedException)
        {
            if (client == AuthClient.Web) RefreshCookie.Clear(Response);
            throw;
        }
    }

    /// <summary>Signs out: revokes the refresh token (and its family) and clears the cookie.</summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(RefreshRequest? request, CancellationToken cancellationToken)
    {
        var (token, client) = ResolveRefreshToken(request);
        await auth.LogoutAsync(token, cancellationToken);
        if (client == AuthClient.Web) RefreshCookie.Clear(Response);
        return NoContent();
    }

    /// <summary>The signed-in user with current roles, permissions and warehouses.</summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType<CurrentUserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public Task<CurrentUserDto> Me(CancellationToken cancellationToken) => auth.GetCurrentUserAsync(currentUser.Id, cancellationToken);

    /// <summary>A token in the body means the mobile client; otherwise the web cookie is used.</summary>
    private (string? Token, AuthClient Client) ResolveRefreshToken(RefreshRequest? request) =>
        string.IsNullOrWhiteSpace(request?.RefreshToken)
            ? (RefreshCookie.Read(Request), AuthClient.Web)
            : (request!.RefreshToken, AuthClient.Mobile);

    private AuthResponse ToResponse(AuthResult result, AuthClient client)
    {
        if (client == AuthClient.Web)
        {
            RefreshCookie.Set(Response, result.RefreshToken, result.RefreshTokenExpiresAt);
            return new AuthResponse(result.AccessToken, result.AccessTokenExpiresAt, null, result.User);
        }
        return new AuthResponse(result.AccessToken, result.AccessTokenExpiresAt, result.RefreshToken, result.User);
    }
}
