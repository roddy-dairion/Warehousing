using FluentValidation;

namespace Inventory.Application.Customers;

public sealed class AddressRequestValidator : AbstractValidator<AddressRequest>
{
    public AddressRequestValidator()
    {
        RuleFor(x => x.Label).NotEmpty().WithMessage("Enter a label, e.g. Head office.").MaximumLength(60);
        RuleFor(x => x.Line1).NotEmpty().WithMessage("Enter the address.").MaximumLength(200);
        RuleFor(x => x.Line2).MaximumLength(200);
        RuleFor(x => x.City).MaximumLength(100);
        RuleFor(x => x.Country).MaximumLength(100);
        RuleFor(x => x.ContactName).MaximumLength(120);
        RuleFor(x => x.Phone).MaximumLength(40);
    }
}

public sealed class CreateCustomerRequestValidator : AbstractValidator<CreateCustomerRequest>
{
    public CreateCustomerRequestValidator()
    {
        RuleFor(x => x.Reference).NotEmpty().WithMessage("Enter a reference.").Matches("^[A-Za-z0-9][A-Za-z0-9-]{0,31}$").WithMessage("1–32 letters, digits or hyphens.");
        RuleFor(x => x.CompanyName).NotEmpty().WithMessage("Enter the company name.").MaximumLength(200);
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email)).WithMessage("Enter a valid email address.");
        RuleFor(x => x.Notes).MaximumLength(2000);
        RuleForEach(x => x.Addresses).SetValidator(new AddressRequestValidator());
    }
}

public sealed class UpdateCustomerRequestValidator : AbstractValidator<UpdateCustomerRequest>
{
    public UpdateCustomerRequestValidator()
    {
        RuleFor(x => x.CompanyName).NotEmpty().WithMessage("Enter the company name.").MaximumLength(200);
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email)).WithMessage("Enter a valid email address.");
        RuleFor(x => x.Notes).MaximumLength(2000);
    }
}

public sealed class AddressChangeRequestValidator : AbstractValidator<AddressChangeRequest>
{
    public AddressChangeRequestValidator()
    {
        RuleFor(x => x.Address).NotNull().SetValidator(new AddressRequestValidator());
    }
}
