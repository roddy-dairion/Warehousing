using Inventory.Api.Infrastructure;
using Inventory.Application.Common;
using Inventory.Application.Users;
using Inventory.Domain.Security;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers;

[ApiController]
[Route("api/v1/users")]
[Produces("application/json")]
[HasPermission(Permissions.UsersManage)]
public sealed class UsersController(IUserAdminService users) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<UserListItemDto>> List([FromQuery] UserListQuery query, CancellationToken ct) => users.ListAsync(query, ct);

    [HttpGet("{id:guid}")]
    public Task<UserDto> Get(Guid id, CancellationToken ct) => users.GetAsync(id, ct);

    [HttpPost]
    [ProducesResponseType<UserDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<UserDto>> Create(CreateUserRequest request, CancellationToken ct)
    {
        var created = await users.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    public Task<UserDto> Update(Guid id, UpdateUserRequest request, CancellationToken ct) => users.UpdateAsync(id, request, ct);

    [HttpPost("{id:guid}/activate")]
    public async Task<IActionResult> Activate(Guid id, CancellationToken ct)
    {
        await users.SetActiveAsync(id, true, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        await users.SetActiveAsync(id, false, ct);
        return NoContent();
    }

    /// <summary>Sets a new password, unlocks the account and signs the user out everywhere.</summary>
    [HttpPost("{id:guid}/reset-password")]
    public async Task<IActionResult> ResetPassword(Guid id, ResetPasswordRequest request, CancellationToken ct)
    {
        await users.ResetPasswordAsync(id, request, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/unlock")]
    public async Task<IActionResult> Unlock(Guid id, CancellationToken ct)
    {
        await users.UnlockAsync(id, ct);
        return NoContent();
    }
}
