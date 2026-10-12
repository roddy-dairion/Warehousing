using Inventory.Domain.Common;
using Inventory.Domain.Dispatches;
using Inventory.Domain.Documents;

namespace Inventory.Tests;

public sealed class DispatchDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 8, 0, 0, TimeSpan.Zero);
    private static readonly AddressSnapshot Address = new("Shop", "1 Main Street", null, "Port Louis", "Mauritius", "Jane", null);

    private static Dispatch Draft(Guid? creator = null, params decimal[] quantities)
    {
        var d = Dispatch.Create("DSP-1", Guid.NewGuid(), Guid.NewGuid(), null, new DateOnly(2026, 10, 12), null, null, creator, Now);
        d.CreatedBy = creator;
        d.ReplaceLines((quantities.Length == 0 ? [5m] : quantities).Select(q => DispatchLine.Create(Guid.NewGuid(), null, q, q, null)));
        return d;
    }

    private static Dispatch Approved(params decimal[] quantities)
    {
        var d = Draft(Guid.NewGuid(), quantities);
        d.Submit(Address, Guid.NewGuid(), Now);
        d.Approve(Guid.NewGuid(), Now, requireDifferentApprover: true, null);
        return d;
    }

    [Fact]
    public void Happy_path_records_every_transition()
    {
        var d = Approved(5);
        d.StartPicking(null, null, Now);
        d.Lines[0].AddPicked(5);
        d.CompletePicking(allowShort: false, null, Now);
        d.Ship("Van", "T1", null, null, Now);
        d.MarkDelivered(null, Now);
        d.History.Select(h => h.ToStatus).ShouldBe([DispatchStatus.Draft, DispatchStatus.Submitted, DispatchStatus.Approved, DispatchStatus.Picking,
            DispatchStatus.ReadyForDispatch, DispatchStatus.Dispatched, DispatchStatus.Delivered]);
        d.Lines[0].QuantityShipped.ShouldBe(5);
        d.ShortDecision.ShouldBeNull();
    }

    [Fact]
    public void Invalid_transitions_are_rejected()
    {
        var d = Draft();
        Should.Throw<DomainRuleException>(() => d.Approve(null, Now, false, null)).Code.ShouldBe("dispatch.invalid_status");
        Should.Throw<DomainRuleException>(() => d.Ship(null, null, null, null, Now)).Code.ShouldBe("dispatch.invalid_status");
        Should.Throw<DomainRuleException>(() => d.MarkDelivered(null, Now)).Code.ShouldBe("dispatch.invalid_status");
        d.Submit(Address, null, Now);
        Should.Throw<DomainRuleException>(() => d.ReplaceLines([])).Code.ShouldBe("dispatch.invalid_status");
    }

    [Fact]
    public void Creator_or_submitter_cannot_approve_when_required()
    {
        var creator = Guid.NewGuid();
        var d = Draft(creator);
        d.Submit(Address, creator, Now);
        Should.Throw<DomainRuleException>(() => d.Approve(creator, Now, requireDifferentApprover: true, null)).Code.ShouldBe("dispatch.self_approval");
        d.Approve(creator, Now, requireDifferentApprover: false, null);
        d.Status.ShouldBe(DispatchStatus.Approved);
    }

    [Fact]
    public void Lines_cannot_repeat_a_product_and_drafts_need_lines()
    {
        var product = Guid.NewGuid();
        var d = Draft();
        Should.Throw<DomainRuleException>(() => d.ReplaceLines([DispatchLine.Create(product, null, 1, 1, null), DispatchLine.Create(product, null, 2, 2, null)]))
            .Code.ShouldBe("dispatch.duplicate_product");
        d.ReplaceLines([]);
        Should.Throw<DomainRuleException>(() => d.Submit(Address, null, Now)).Code.ShouldBe("dispatch.empty");
    }

    [Fact]
    public void Picking_cannot_exceed_the_reserved_quantity()
    {
        var d = Approved(5);
        var line = d.Lines[0];
        line.AddPicked(3);
        Should.Throw<DomainRuleException>(() => line.AddPicked(3)).Code.ShouldBe("pick.over_pick");
        Should.Throw<DomainRuleException>(() => line.AddPicked(-4)).Code.ShouldBe("pick.negative");
        line.AddPicked(-3);
        line.QuantityPicked.ShouldBe(0);
    }

    [Fact]
    public void Short_picking_needs_permission_and_short_shipment_needs_a_decision()
    {
        var d = Approved(10, 4);
        d.StartPicking(null, null, Now);
        Should.Throw<DomainRuleException>(() => d.CompletePicking(true, null, Now)).Code.ShouldBe("dispatch.nothing_picked");
        d.Lines[0].AddPicked(6);
        d.Lines[1].AddPicked(4);
        Should.Throw<DomainRuleException>(() => d.CompletePicking(false, null, Now)).Code.ShouldBe("dispatch.picking_incomplete");
        d.CompletePicking(true, null, Now);
        Should.Throw<DomainRuleException>(() => d.Ship(null, null, null, null, Now)).Code.ShouldBe("dispatch.short_decision_required");
        d.Ship(null, null, ShortShipDecision.Backorder, null, Now);
        d.Lines[0].Outstanding.ShouldBe(4);
        d.Lines[1].Outstanding.ShouldBe(0);

        var child = Dispatch.CreateBackorder("DSP-2", d, [DispatchLine.Create(d.Lines[0].ProductId, null, 4, 4, null)], null, Now);
        child.Status.ShouldBe(DispatchStatus.Approved);
        child.BackorderOfId.ShouldBe(d.Id);
        child.Lines.Single().QuantityReserved.ShouldBe(4);
        child.ShipTo.ShouldBe(d.ShipTo);
        child.ShipTo.ShouldNotBeSameAs(d.ShipTo);
    }

    [Fact]
    public void Shipped_dispatches_cannot_be_cancelled_and_cancel_needs_a_reason()
    {
        var d = Approved(1);
        Should.Throw<DomainRuleException>(() => d.Cancel(null, Now, " ")).Code.ShouldBe("dispatch.reason_required");
        d.StartPicking(null, null, Now);
        d.Lines[0].AddPicked(1);
        d.CompletePicking(false, null, Now);
        d.HoldsReservations.ShouldBeTrue();
        d.Ship(null, null, null, null, Now);
        Should.Throw<DomainRuleException>(() => d.Cancel(null, Now, "late")).Code.ShouldBe("dispatch.cannot_cancel_shipped");

        var other = Approved(1);
        other.Cancel(null, Now, "Customer changed their mind");
        other.Status.ShouldBe(DispatchStatus.Cancelled);
        Should.Throw<DomainRuleException>(() => other.Cancel(null, Now, "again")).Code.ShouldBe("dispatch.already_cancelled");
    }

    [Fact]
    public void Proof_of_delivery_needs_a_receiver_and_no_future_time()
    {
        Should.Throw<DomainRuleException>(() => ProofOfDelivery.Create(Guid.NewGuid(), " ", Now, Guid.NewGuid(), null, null, Now)).Code.ShouldBe("pod.receiver_required");
        Should.Throw<DomainRuleException>(() => ProofOfDelivery.Create(Guid.NewGuid(), "John", Now.AddHours(1), Guid.NewGuid(), null, null, Now)).Code.ShouldBe("pod.future");
        ProofOfDelivery.Create(Guid.NewGuid(), "John", Now.AddMinutes(-10), Guid.NewGuid(), null, null, Now).ReceiverName.ShouldBe("John");
    }

    [Fact]
    public void Scan_ids_are_required()
    {
        Should.Throw<DomainRuleException>(() => PickConfirmation.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), " ", Guid.NewGuid(), null, 1, null, null, null, Now))
            .Code.ShouldBe("pick.scan_id_invalid");
    }

    [Fact]
    public void Email_retries_back_off_and_give_up()
    {
        var m = EmailMessage.Create("a@b.test", null, "S", "B", [], "dispatch", Guid.NewGuid(), Now);
        m.MarkFailed("smtp down", Now);
        m.NextAttemptAt.ShouldBe(Now.AddMinutes(1));
        m.MarkFailed("smtp down", Now);
        m.NextAttemptAt.ShouldBe(Now.AddMinutes(2));
        for (var i = 2; i < EmailMessage.MaxAttempts; i++) m.MarkFailed("smtp down", Now);
        m.Status.ShouldBe(EmailStatus.Failed);
    }

    [Fact]
    public void Company_time_zone_must_exist()
    {
        var c = CompanyProfile.Create("HelloCherie");
        Should.Throw<DomainRuleException>(() => c.Update("HelloCherie", null, null, null, null, null, null, null, null, null, null, "Nowhere/Land"))
            .Code.ShouldBe("company.time_zone_invalid");
        c.Update("HelloCherie", null, null, null, null, null, "dispatch@hellocherie.ch", null, null, null, null, "Europe/Zurich");
        c.TimeZone.ShouldBe("Europe/Zurich");
    }
}
