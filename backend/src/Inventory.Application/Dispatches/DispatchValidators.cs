using FluentValidation;
using Inventory.Domain.Dispatches;
using Inventory.Domain.Inventory;

namespace Inventory.Application.Dispatches;

public sealed class DispatchLineRequestValidator : AbstractValidator<DispatchLineRequest>
{
    public DispatchLineRequestValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty().WithMessage("Choose a product.");
        RuleFor(x => x.Quantity).GreaterThan(0).WithMessage("Quantity must be greater than zero.");
        RuleFor(x => x.Notes).MaximumLength(500);
    }
}

public sealed class CreateDispatchRequestValidator : AbstractValidator<CreateDispatchRequest>
{
    public CreateDispatchRequestValidator()
    {
        RuleFor(x => x.WarehouseId).NotEmpty().WithMessage("Choose a warehouse.");
        RuleFor(x => x.CustomerId).NotEmpty().WithMessage("Choose a customer.");
        RuleFor(x => x.CustomerOrderReference).MaximumLength(60);
        RuleFor(x => x.Notes).MaximumLength(2000);
        RuleFor(x => x.Lines!.Count).LessThanOrEqualTo(Dispatch.MaxLines).When(x => x.Lines is not null).OverridePropertyName("lines");
        RuleForEach(x => x.Lines).SetValidator(new DispatchLineRequestValidator());
    }
}

public sealed class UpdateDispatchRequestValidator : AbstractValidator<UpdateDispatchRequest>
{
    public UpdateDispatchRequestValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty().WithMessage("Choose a customer.");
        RuleFor(x => x.CustomerOrderReference).MaximumLength(60);
        RuleFor(x => x.Notes).MaximumLength(2000);
        RuleFor(x => x.Lines).NotNull();
        RuleFor(x => x.Lines.Count).LessThanOrEqualTo(Dispatch.MaxLines).When(x => x.Lines is not null).OverridePropertyName("lines");
        RuleForEach(x => x.Lines).SetValidator(new DispatchLineRequestValidator());
    }
}

public sealed class DispatchReasonRequestValidator : AbstractValidator<DispatchReasonRequest>
{
    public DispatchReasonRequestValidator() => RuleFor(x => x.Reason).NotEmpty().WithMessage("Enter a reason.").MaximumLength(500);
}

public sealed class PickRequestValidator : AbstractValidator<PickRequest>
{
    public PickRequestValidator()
    {
        RuleFor(x => x.ScanId).NotEmpty().MaximumLength(PickConfirmation.ScanIdMaxLength);
        RuleFor(x => x.Location).NotEmpty().WithMessage("Scan or enter the bin.").MaximumLength(64);
        RuleFor(x => x).Must(x => !string.IsNullOrWhiteSpace(x.Barcode) || x.ProductId is not null)
            .WithMessage("Scan the product barcode or choose the product.").OverridePropertyName("barcode");
        RuleFor(x => x.Barcode).MaximumLength(64);
        RuleFor(x => x.LotNo).MaximumLength(ProductLot.LotNoMaxLength);
        RuleFor(x => x.Quantity).GreaterThan(0).When(x => x.Quantity is not null).WithMessage("Quantity must be greater than zero.");
    }
}

public sealed class UndoPickRequestValidator : AbstractValidator<UndoPickRequest>
{
    public UndoPickRequestValidator() => RuleFor(x => x.ScanId).NotEmpty().MaximumLength(PickConfirmation.ScanIdMaxLength);
}

public sealed class ReallocateRequestValidator : AbstractValidator<ReallocateRequest>
{
    public ReallocateRequestValidator()
    {
        RuleFor(x => x.ReservationId).NotEmpty();
        RuleFor(x => x.Location).NotEmpty().WithMessage("Scan or enter the bin.").MaximumLength(64);
        RuleFor(x => x.Quantity).GreaterThan(0).WithMessage("Quantity must be greater than zero.");
        RuleFor(x => x.LotNo).MaximumLength(ProductLot.LotNoMaxLength);
    }
}

public sealed class DiscrepancyRequestValidator : AbstractValidator<DiscrepancyRequest>
{
    public DiscrepancyRequestValidator()
    {
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Quantity).GreaterThan(0).When(x => x.Quantity is not null);
        RuleFor(x => x.Note).NotEmpty().When(x => x.Type == DiscrepancyType.Other).WithMessage("Describe the problem.").MaximumLength(500);
        RuleFor(x => x.Barcode).MaximumLength(64);
        RuleFor(x => x.Location).MaximumLength(64);
    }
}

public sealed class ShipRequestValidator : AbstractValidator<ShipRequest>
{
    public ShipRequestValidator()
    {
        RuleFor(x => x.Carrier).MaximumLength(100);
        RuleFor(x => x.TrackingNumber).MaximumLength(100);
        RuleFor(x => x.ShortDecision).IsInEnum().When(x => x.ShortDecision is not null);
    }
}

public sealed class SendDeliveryNoteRequestValidator : AbstractValidator<SendDeliveryNoteRequest>
{
    public SendDeliveryNoteRequestValidator() =>
        RuleFor(x => x.To).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.To)).WithMessage("Enter a valid email address.");
}
