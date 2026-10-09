using FluentValidation;

namespace Inventory.Application.Warehouses;

public sealed class CreateWarehouseRequestValidator : AbstractValidator<CreateWarehouseRequest>
{
    public CreateWarehouseRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().WithMessage("Enter a code.").Matches("^[A-Za-z0-9][A-Za-z0-9-]{1,15}$").WithMessage("2–16 letters, digits or hyphens.");
        RuleFor(x => x.Name).NotEmpty().WithMessage("Enter a name.").MaximumLength(120);
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email)).WithMessage("Enter a valid email address.");
        RuleFor(x => x.Phone).MaximumLength(40);
    }
}

public sealed class UpdateWarehouseRequestValidator : AbstractValidator<UpdateWarehouseRequest>
{
    public UpdateWarehouseRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Enter a name.").MaximumLength(120);
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email)).WithMessage("Enter a valid email address.");
        RuleFor(x => x.Phone).MaximumLength(40);
    }
}

public sealed class CreateLocationRequestValidator : AbstractValidator<CreateLocationRequest>
{
    public CreateLocationRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().WithMessage("Enter a bin code.").Matches("^[A-Za-z0-9][A-Za-z0-9._-]{0,31}$").WithMessage("1–32 letters, digits, dots, hyphens or underscores.");
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Zone).MaximumLength(32);
        RuleFor(x => x.Barcode).MaximumLength(64);
    }
}

public sealed class UpdateLocationRequestValidator : AbstractValidator<UpdateLocationRequest>
{
    public UpdateLocationRequestValidator()
    {
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Zone).MaximumLength(32);
        RuleFor(x => x.Barcode).MaximumLength(64);
    }
}
