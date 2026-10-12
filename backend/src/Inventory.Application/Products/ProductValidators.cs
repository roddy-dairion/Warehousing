using FluentValidation;

namespace Inventory.Application.Products;

public sealed class CreateProductRequestValidator : AbstractValidator<CreateProductRequest>
{
    public CreateProductRequestValidator()
    {
        RuleFor(x => x.Sku).NotEmpty().WithMessage("Enter a SKU.").Matches(@"^[A-Za-z0-9][A-Za-z0-9._/-]{0,63}$").WithMessage("1–64 letters, digits, dots, slashes, hyphens or underscores.");
        RuleFor(x => x.Name).NotEmpty().WithMessage("Enter a name.").MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.BaseUnitId).NotEmpty().WithMessage("Choose a base unit.");
        RuleFor(x => x.MinStockQty).GreaterThanOrEqualTo(0).When(x => x.MinStockQty is not null);
        RuleFor(x => x.MaxStockQty).GreaterThanOrEqualTo(0).When(x => x.MaxStockQty is not null);
        RuleFor(x => x.ShelfLifeDays).InclusiveBetween(1, 36500).When(x => x.ShelfLifeDays is not null);
        RuleForEach(x => x.Units).ChildRules(u => u.RuleFor(x => x.FactorToBase).GreaterThan(0));
        RuleForEach(x => x.Barcodes).ChildRules(b => b.RuleFor(x => x.Value).NotEmpty().MaximumLength(64));
    }
}

public sealed class UpdateProductRequestValidator : AbstractValidator<UpdateProductRequest>
{
    public UpdateProductRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Enter a name.").MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.BaseUnitId).NotEmpty().WithMessage("Choose a base unit.");
        RuleFor(x => x.MinStockQty).GreaterThanOrEqualTo(0).When(x => x.MinStockQty is not null);
        RuleFor(x => x.MaxStockQty).GreaterThanOrEqualTo(0).When(x => x.MaxStockQty is not null);
        RuleFor(x => x.ShelfLifeDays).InclusiveBetween(1, 36500).When(x => x.ShelfLifeDays is not null);
    }
}

public sealed class AddBarcodeRequestValidator : AbstractValidator<AddBarcodeRequest>
{
    public AddBarcodeRequestValidator()
    {
        RuleFor(x => x.Value).NotEmpty().WithMessage("Enter or scan a barcode.").MaximumLength(64);
        RuleFor(x => x.Symbology).MaximumLength(20);
    }
}

public sealed class CreateCategoryRequestValidator : AbstractValidator<CreateCategoryRequest>
{
    public CreateCategoryRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().Matches("^[A-Za-z0-9][A-Za-z0-9-]{0,31}$").WithMessage("1–32 letters, digits or hyphens.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
    }
}

public sealed class CreateUnitRequestValidator : AbstractValidator<CreateUnitRequest>
{
    public CreateUnitRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().Matches("^[A-Za-z0-9]{1,10}$").WithMessage("1–10 letters or digits.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(60);
        RuleFor(x => x.Decimals).InclusiveBetween(0, 3);
    }
}
