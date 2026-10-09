using FluentValidation;
using Inventory.Application.Auth;
using Inventory.Application.Common;

namespace Inventory.Application.Users;

public sealed record UserListItemDto(
    Guid Id,
    string Email,
    string DisplayName,
    IReadOnlyList<string> Roles,
    bool AllWarehouses,
    IReadOnlyList<string> WarehouseCodes,
    bool IsActive,
    bool IsLockedOut,
    DateTimeOffset? LastLoginAt);

public sealed record UserDto(
    Guid Id,
    string Email,
    string DisplayName,
    IReadOnlyList<string> Roles,
    bool AllWarehouses,
    IReadOnlyList<WarehouseSummaryDto> Warehouses,
    bool IsActive,
    bool IsLockedOut,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt,
    string ConcurrencyStamp);

public sealed class UserListQuery : ListQuery
{
    public string? Role { get; set; }
}

public sealed record CreateUserRequest(
    string Email,
    string DisplayName,
    string Password,
    IReadOnlyList<string> Roles,
    bool AllWarehouses,
    IReadOnlyList<Guid>? WarehouseIds);

public sealed record UpdateUserRequest(
    string DisplayName,
    IReadOnlyList<string> Roles,
    bool AllWarehouses,
    IReadOnlyList<Guid>? WarehouseIds,
    string ConcurrencyStamp);

public sealed record ResetPasswordRequest(string NewPassword);

public interface IUserAdminService
{
    Task<PagedResult<UserListItemDto>> ListAsync(UserListQuery query, CancellationToken cancellationToken);
    Task<UserDto> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<UserDto> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken);
    Task<UserDto> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken cancellationToken);
    Task SetActiveAsync(Guid id, bool active, CancellationToken cancellationToken);
    Task ResetPasswordAsync(Guid id, ResetPasswordRequest request, CancellationToken cancellationToken);
    Task UnlockAsync(Guid id, CancellationToken cancellationToken);
}

public sealed class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().WithMessage("Enter an email address.").EmailAddress().WithMessage("Enter a valid email address.").MaximumLength(256);
        RuleFor(x => x.DisplayName).NotEmpty().WithMessage("Enter a name.").MaximumLength(120);
        RuleFor(x => x.Password).NotEmpty().WithMessage("Enter an initial password.").MinimumLength(10).WithMessage("At least 10 characters.").MaximumLength(128);
        RuleFor(x => x.Roles).NotEmpty().WithMessage("Choose at least one role.");
    }
}

public sealed class UpdateUserRequestValidator : AbstractValidator<UpdateUserRequest>
{
    public UpdateUserRequestValidator()
    {
        RuleFor(x => x.DisplayName).NotEmpty().WithMessage("Enter a name.").MaximumLength(120);
        RuleFor(x => x.Roles).NotEmpty().WithMessage("Choose at least one role.");
        RuleFor(x => x.ConcurrencyStamp).NotEmpty();
    }
}

public sealed class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator()
    {
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(10).WithMessage("At least 10 characters.").MaximumLength(128);
    }
}
