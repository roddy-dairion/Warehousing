using Inventory.Api.Infrastructure;
using Inventory.Application.Roles;
using Inventory.Domain.Security;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers;

/// <summary>Roles and the permission catalogue (for the user-management screens).</summary>
[ApiController]
[Route("api/v1")]
[Produces("application/json")]
[HasPermission(Permissions.UsersManage)]
public sealed class RolesController(IRoleQueries queries) : ControllerBase
{
    [HttpGet("roles")]
    public Task<IReadOnlyList<RoleDto>> ListRoles(CancellationToken cancellationToken) => queries.ListAsync(cancellationToken);

    [HttpGet("permissions")]
    public Task<IReadOnlyList<PermissionDto>> ListPermissions(CancellationToken cancellationToken) => queries.ListPermissionsAsync(cancellationToken);
}
