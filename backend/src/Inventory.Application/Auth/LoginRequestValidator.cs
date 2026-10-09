using FluentValidation;

namespace Inventory.Application.Auth;

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().WithMessage("Enter your email address.").MaximumLength(256).EmailAddress().WithMessage("Enter a valid email address.");
        RuleFor(x => x.Password).NotEmpty().WithMessage("Enter your password.").MaximumLength(256);
        RuleFor(x => x.Client).IsInEnum();
    }
}
