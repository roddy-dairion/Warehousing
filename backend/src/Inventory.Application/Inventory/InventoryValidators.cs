using FluentValidation;
using Inventory.Domain.Inventory;

namespace Inventory.Application.Inventory;

public sealed class ReceiptLineRequestValidator : AbstractValidator<ReceiptLineRequest>
{
    public ReceiptLineRequestValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty().WithMessage("Choose a product.");
        RuleFor(x => x.Quantity).GreaterThan(0).WithMessage("Quantity must be greater than zero.");
        RuleFor(x => x.ExpectedQuantity).GreaterThanOrEqualTo(0).When(x => x.ExpectedQuantity is not null);
        RuleFor(x => x.LotNo).MaximumLength(ProductLot.LotNoMaxLength);
        RuleFor(x => x.Notes).MaximumLength(500);
    }
}

public sealed class CreateReceiptRequestValidator : AbstractValidator<CreateReceiptRequest>
{
    public CreateReceiptRequestValidator()
    {
        RuleFor(x => x.WarehouseId).NotEmpty().WithMessage("Choose a warehouse.");
        RuleFor(x => x.SupplierName).MaximumLength(200);
        RuleFor(x => x.SupplierReference).MaximumLength(60);
        RuleFor(x => x.Notes).MaximumLength(2000);
        RuleFor(x => x.Lines!.Count).LessThanOrEqualTo(GoodsReceipt.MaxLines).When(x => x.Lines is not null).OverridePropertyName("lines");
        RuleForEach(x => x.Lines).SetValidator(new ReceiptLineRequestValidator());
    }
}

public sealed class UpdateReceiptRequestValidator : AbstractValidator<UpdateReceiptRequest>
{
    public UpdateReceiptRequestValidator()
    {
        RuleFor(x => x.SupplierName).MaximumLength(200);
        RuleFor(x => x.SupplierReference).MaximumLength(60);
        RuleFor(x => x.Notes).MaximumLength(2000);
        RuleFor(x => x.Lines).NotNull();
        RuleFor(x => x.Lines.Count).LessThanOrEqualTo(GoodsReceipt.MaxLines).When(x => x.Lines is not null).OverridePropertyName("lines");
        RuleForEach(x => x.Lines).SetValidator(new ReceiptLineRequestValidator());
    }
}

public sealed class ReasonRequestValidator : AbstractValidator<ReasonRequest>
{
    public ReasonRequestValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().WithMessage("Enter a reason.").MaximumLength(500);
    }
}

public sealed class AdjustmentLineRequestValidator : AbstractValidator<AdjustmentLineRequest>
{
    public AdjustmentLineRequestValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty().WithMessage("Choose a product.");
        RuleFor(x => x.LocationId).NotEmpty().WithMessage("Choose a bin.");
        RuleFor(x => x.QuantityDelta).NotEqual(0).WithMessage("Enter a quantity to add (positive) or remove (negative).");
        RuleFor(x => x.LotNo).MaximumLength(ProductLot.LotNoMaxLength);
        RuleFor(x => x.Notes).MaximumLength(500);
    }
}

public sealed class CreateAdjustmentRequestValidator : AbstractValidator<CreateAdjustmentRequest>
{
    public CreateAdjustmentRequestValidator()
    {
        RuleFor(x => x.WarehouseId).NotEmpty().WithMessage("Choose a warehouse.");
        RuleFor(x => x.Reason).IsInEnum().WithMessage("Choose a reason.");
        RuleFor(x => x.Notes).MaximumLength(2000);
        RuleFor(x => x.Notes).NotEmpty().When(x => x.Reason == AdjustmentReason.Other).WithMessage("Explain the adjustment when the reason is Other.");
        RuleForEach(x => x.Lines).SetValidator(new AdjustmentLineRequestValidator());
    }
}

public sealed class UpdateAdjustmentRequestValidator : AbstractValidator<UpdateAdjustmentRequest>
{
    public UpdateAdjustmentRequestValidator()
    {
        RuleFor(x => x.Reason).IsInEnum().WithMessage("Choose a reason.");
        RuleFor(x => x.Notes).MaximumLength(2000);
        RuleFor(x => x.Notes).NotEmpty().When(x => x.Reason == AdjustmentReason.Other).WithMessage("Explain the adjustment when the reason is Other.");
        RuleFor(x => x.Lines).NotNull();
        RuleForEach(x => x.Lines).SetValidator(new AdjustmentLineRequestValidator());
    }
}

public sealed class MoveLineRequestValidator : AbstractValidator<MoveLineRequest>
{
    public MoveLineRequestValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty().WithMessage("Choose a product.");
        RuleFor(x => x.FromLocationId).NotEmpty().WithMessage("Choose the bin to move from.");
        RuleFor(x => x.ToLocationId).NotEmpty().WithMessage("Choose the bin to move to.");
        RuleFor(x => x.ToLocationId).NotEqual(x => x.FromLocationId).WithMessage("The source and destination bins must be different.");
        RuleFor(x => x.Quantity).GreaterThan(0).WithMessage("Quantity must be greater than zero.");
        RuleFor(x => x.LotNo).MaximumLength(ProductLot.LotNoMaxLength);
    }
}

public sealed class CreateBinMoveRequestValidator : AbstractValidator<CreateBinMoveRequest>
{
    public CreateBinMoveRequestValidator()
    {
        RuleFor(x => x.WarehouseId).NotEmpty().WithMessage("Choose a warehouse.");
        RuleFor(x => x.Notes).MaximumLength(2000);
        RuleFor(x => x.Lines).NotEmpty().WithMessage("Add at least one line.");
        RuleFor(x => x.Lines.Count).LessThanOrEqualTo(StockTransfer.MaxLines).When(x => x.Lines is not null).OverridePropertyName("lines");
        RuleForEach(x => x.Lines).SetValidator(new MoveLineRequestValidator());
    }
}

public sealed class SetLotStatusRequestValidator : AbstractValidator<SetLotStatusRequest>
{
    public SetLotStatusRequestValidator()
    {
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.Reason).NotEmpty().When(x => x.Status != LotStatus.Released).WithMessage("Enter a reason for quarantining or blocking the lot.");
        RuleFor(x => x.Reason).MaximumLength(500);
    }
}
