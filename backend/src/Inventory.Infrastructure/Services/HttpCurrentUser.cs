using System.Security.Claims;
using Inventory.Application.Common;
using Inventory.Infrastructure.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Inventory.Infrastructure.Services;

internal sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public Guid? IdOrNull =>
        IsAuthenticated && Guid.TryParse(Principal!.FindFirstValue(JwtRegisteredClaimNames.Sub), out var id) ? id : null;

    public Guid Id => IdOrNull ?? throw new AuthenticationFailedException("Not signed in.");

    public bool HasPermission(string permission) => Principal?.HasClaim(CustomClaims.Permission, permission) == true;

    public string? IpAddress => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public string? CorrelationId => accessor.HttpContext?.TraceIdentifier;
}
