using Inventory.Domain.Common;
using Inventory.Domain.Inventory;

namespace Inventory.Tests;

public sealed class InventoryDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 8, 0, 0, TimeSpan.Zero);

    private static InventoryBalance Balance(decimal onHand = 0, decimal reserved = 0)
    {
        var b = (InventoryBalance)Activator.CreateInstance(typeof(InventoryBalance), nonPublic: true)!;
        b.Apply(onHand, reserved, Now);
        return b;
    }

    [Fact]
    public void Balance_keeps_available_equal_to_on_hand_minus_reserved()
    {
        var b = Balance(10);
        b.Apply(0, 4, Now);
        (b.OnHand, b.Reserved, b.Available).ShouldBe((10m, 4m, 6m));
        b.Apply(-3, -3, Now.AddMinutes(1));
        (b.OnHand, b.Reserved, b.Available).ShouldBe((7m, 1m, 6m));
        b.LastMovementAt.ShouldBe(Now.AddMinutes(1));
    }

    [Theory]
    [InlineData(-11, 0, "stock.insufficient")]
    [InlineData(0, 11, "stock.reserved_exceeds_on_hand")]
    [InlineData(0, -1, "stock.reservation_negative")]
    public void Balance_rejects_changes_that_break_the_invariants(decimal onHandDelta, decimal reservedDelta, string code)
    {
        var b = Balance(10);
        var e = Should.Throw<DomainRuleException>(() => b.Apply(onHandDelta, reservedDelta, Now));
        e.Code.ShouldBe(code);
        e.IsConflict.ShouldBeTrue();
        (b.OnHand, b.Reserved).ShouldBe((10m, 0m));
    }

    [Fact]
    public void On_hand_cannot_drop_below_reserved()
    {
        var b = Balance(10, 8);
        Should.Throw<DomainRuleException>(() => b.Apply(-3, 0, Now)).Code.ShouldBe("stock.reserved_exceeds_on_hand");
        b.Apply(-2, 0, Now);
        b.Available.ShouldBe(0);
    }

    [Fact]
    public void Reservation_tracks_consumed_and_released_quantities()
    {
        var r = StockReservation.Create(Balance(10), "dispatch", Guid.NewGuid(), Guid.NewGuid(), 8);
        r.Consume(5);
        r.Outstanding.ShouldBe(3);
        r.Status.ShouldBe(ReservationStatus.Open);
        Should.Throw<DomainRuleException>(() => r.Consume(4)).Code.ShouldBe("reservation.consume");
        r.Release().ShouldBe(3);
        r.Status.ShouldBe(ReservationStatus.Consumed);
        r.Outstanding.ShouldBe(0);

        var unused = StockReservation.Create(Balance(10), "dispatch", Guid.NewGuid(), Guid.NewGuid(), 2);
        unused.Release();
        unused.Status.ShouldBe(ReservationStatus.Released);
        Should.Throw<DomainRuleException>(() => StockReservation.Create(Balance(1), "dispatch", Guid.NewGuid(), Guid.NewGuid(), 0));
    }

    [Theory]
    [InlineData(" ab-12 ", "AB-12")]
    [InlineData("lot.7", "LOT.7")]
    public void Lot_numbers_are_normalised(string input, string expected) => ProductLot.NormalizeLotNo(input).ShouldBe(expected);

    [Theory]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("12345678901234567890123456789012345678901")]
    public void Invalid_lot_numbers_are_rejected(string input) =>
        Should.Throw<DomainRuleException>(() => ProductLot.NormalizeLotNo(input)).Code.ShouldBe("lot.number_invalid");

    [Fact]
    public void Lot_expiry_is_set_once_and_drives_reservability()
    {
        var lot = ProductLot.Create(Guid.NewGuid(), "L1", null);
        lot.ConfirmExpiry(new DateOnly(2027, 1, 31));
        lot.ConfirmExpiry(null);
        lot.ConfirmExpiry(new DateOnly(2027, 1, 31));
        Should.Throw<DomainRuleException>(() => lot.ConfirmExpiry(new DateOnly(2027, 2, 1))).Code.ShouldBe("lot.expiry_mismatch");

        lot.IsReservable(new DateOnly(2027, 1, 31)).ShouldBeTrue();
        lot.IsReservable(new DateOnly(2027, 2, 1)).ShouldBeFalse();
        lot.SetStatus(LotStatus.Quarantine);
        lot.IsReservable(new DateOnly(2026, 1, 1)).ShouldBeFalse();
    }

    [Theory]
    [InlineData(1.5, 0, false)]
    [InlineData(2, 0, true)]
    [InlineData(1.25, 3, true)]
    [InlineData(1.2345, 3, false)]
    public void Quantity_precision_follows_the_unit(decimal qty, int decimals, bool ok)
    {
        if (ok) Quantities.Positive(qty, decimals, "U").ShouldBe(qty);
        else Should.Throw<DomainRuleException>(() => Quantities.Positive(qty, decimals, "U")).Code.ShouldBe("quantity.precision");
    }

    [Fact]
    public void Receipt_posts_once_and_only_posted_receipts_reverse()
    {
        var r = GoodsReceipt.Create("GR-1", Guid.NewGuid(), new DateOnly(2026, 10, 11));
        Should.Throw<DomainRuleException>(() => r.MarkPosted(null, Now)).Code.ShouldBe("receipt.empty");
        Should.Throw<DomainRuleException>(() => r.MarkReversed(null, Now, "x")).Code.ShouldBe("receipt.not_posted");

        r.ReplaceLines([GoodsReceiptLine.Create(Guid.NewGuid(), null, 1, 1, null, Guid.NewGuid(), null, null, null)]);
        r.Lines.Single().LineNo.ShouldBe(1);
        r.MarkPosted(Guid.NewGuid(), Now);
        Should.Throw<DomainRuleException>(() => r.MarkPosted(null, Now)).Code.ShouldBe("receipt.not_draft");
        Should.Throw<DomainRuleException>(() => r.ReplaceLines([])).Code.ShouldBe("receipt.not_draft");
        Should.Throw<DomainRuleException>(() => r.MarkReversed(null, Now, " ")).Code.ShouldBe("receipt.reason_required");
        r.MarkReversed(null, Now, "Wrong goods");
        r.Status.ShouldBe(ReceiptStatus.Reversed);
    }

    [Fact]
    public void Adjustment_workflow_enforces_segregation_of_duties()
    {
        var submitter = Guid.NewGuid();
        var a = StockAdjustment.Create("ADJ-1", Guid.NewGuid(), AdjustmentReason.Damage, null);
        Should.Throw<DomainRuleException>(() => a.Submit(submitter, Now)).Code.ShouldBe("adjustment.empty");
        a.ReplaceLines([StockAdjustmentLine.Create(Guid.NewGuid(), Guid.NewGuid(), null, null, -2, null)]);
        a.Submit(submitter, Now);

        Should.Throw<DomainRuleException>(() => a.Approve(submitter, Now, requireDifferentApprover: true, null)).Code.ShouldBe("adjustment.self_approval");
        a.Approve(submitter, Now, requireDifferentApprover: false, "allowed by setting");
        a.Status.ShouldBe(AdjustmentStatus.Posted);
        Should.Throw<DomainRuleException>(() => a.Cancel()).Code.ShouldBe("adjustment.cannot_cancel");
        Should.Throw<DomainRuleException>(() => StockAdjustmentLine.Create(Guid.NewGuid(), Guid.NewGuid(), null, null, 0, null)).Code.ShouldBe("adjustment.zero_line");
    }

    [Fact]
    public void Bin_move_needs_two_different_bins()
    {
        var bin = Guid.NewGuid();
        Should.Throw<DomainRuleException>(() => StockTransferLine.Create(Guid.NewGuid(), null, bin, bin, 1)).Code.ShouldBe("transfer.same_bin");
        Should.Throw<DomainRuleException>(() => StockTransfer.CreateBinMove("MOV-1", Guid.NewGuid(), null, [], null, Now)).Code.ShouldBe("transfer.empty");
        var t = StockTransfer.CreateBinMove("MOV-1", Guid.NewGuid(), null, [StockTransferLine.Create(Guid.NewGuid(), null, bin, Guid.NewGuid(), 3)], null, Now);
        t.FromWarehouseId.ShouldBe(t.ToWarehouseId);
        t.Lines.Single().TransferId.ShouldBe(t.Id);
    }
}
