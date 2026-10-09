namespace Inventory.Application.Roles;

public sealed record PermissionDto(string Code, string Description);

public sealed record RoleDto(Guid Id, string Code, string Name, IReadOnlyList<string> Permissions);

public interface IRoleQueries
{
    Task<IReadOnlyList<RoleDto>> ListAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<PermissionDto>> ListPermissionsAsync(CancellationToken cancellationToken);
}
