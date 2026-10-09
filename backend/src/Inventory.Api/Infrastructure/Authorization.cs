using Inventory.Infrastructure.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Inventory.Api.Infrastructure;

/// <summary>
/// Requires the caller to hold at least one of the permissions, e.g. <c>[HasPermission(Permissions.DispatchesApprove)]</c>.
/// Several attributes on the same target must all be satisfied.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class HasPermissionAttribute(params string[] permissions) : AuthorizeAttribute(PolicyPrefix + string.Join('|', permissions))
{
    public const string PolicyPrefix = "perm:";

    public IReadOnlyList<string> Permissions { get; } = permissions;
}

internal sealed class PermissionRequirement(IReadOnlyList<string> anyOf) : IAuthorizationRequirement
{
    public IReadOnlyList<string> AnyOf { get; } = anyOf;
}

internal sealed class PermissionHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (requirement.AnyOf.Any(p => context.User.HasClaim(CustomClaims.Permission, p)))
        {
            context.Succeed(requirement);
        }
        return Task.CompletedTask;
    }
}

/// <summary>Builds "perm:xyz" policies on demand so each permission does not need registering.</summary>
internal sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : DefaultAuthorizationPolicyProvider(options)
{
    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (policyName.StartsWith(HasPermissionAttribute.PolicyPrefix, StringComparison.Ordinal))
        {
            return new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new PermissionRequirement(policyName[HasPermissionAttribute.PolicyPrefix.Length..].Split('|')))
                .Build();
        }
        return await base.GetPolicyAsync(policyName);
    }
}
