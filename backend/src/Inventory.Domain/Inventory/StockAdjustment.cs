using Inventory.Domain.Common;

namespace Inventory.Domain.Inventory;

public enum AdjustmentReason
{
    CountCorrection,
    Damage,
    Loss,
    Found,
    Expired,
    Other,
}

public enum AdjustmentStatus
{
    Draft,
    PendingApproval,
    Posted,
    Rejected,
    Cancelled,
}

/// <summary>
/// Manual stock correction. Requested by staff, posted only when approved by someone with approval
/// rights (by default not the requester). Posting is the only point where stock changes.
/// </summary>
public sealed class StockAdjustment : Entity, IAuditable
{
    public const int MaxLines = 200;

    private readonly List<StockAdjustmentLine> _lines = [];

    private StockAdjustment() { }

    public string Number { get; private set; } = null!;
    public Guid WarehouseId { get; private set; }
    public AdjustmentReason Reason { get; private set; }
    public string? Notes { get; private set; }
    public AdjustmentStatus Status { get; private set; } = AdjustmentStatus.Draft;
    public DateTimeOffset? SubmittedAt { get; private set; }
    public Guid? SubmittedBy { get; private set; }
    public DateTimeOffset? DecidedAt { get; private set; }
    public Guid? DecidedBy { get; private set; }
    public string? DecisionNote { get; private set; }
    public IReadOnlyList<StockAdjustmentLine> Lines => _lines;

    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public static StockAdjustment Create(string number, Guid warehouseId, AdjustmentReason reason, string? notes)
    {
        var a = new StockAdjustment { Number = number, WarehouseId = warehouseId };
        a.SetHeader(reason, notes);
        return a;
    }

    public void SetHeader(AdjustmentReason reason, string? notes)
    {
        EnsureStatus(AdjustmentStatus.Draft);
        if (!Enum.IsDefined(reason)) throw new DomainRuleException("adjustment.reason_invalid", "Choose a reason.");
        Reason = reason;
        Notes = Text.Optional(notes, 2000, "adjustment.notes_invalid", "Notes");
    }

    public void ReplaceLines(IEnumerable<StockAdjustmentLine> lines)
    {
        EnsureStatus(AdjustmentStatus.Draft);
        var list = lines.ToList();
        if (list.Count > MaxLines) throw new DomainRuleException("adjustment.too_many_lines", $"An adjustment can have at most {MaxLines} lines.");
        _lines.Clear();
        var n = 1;
        foreach (var line in list)
        {
            line.Attach(Id, n++);
            _lines.Add(line);
        }
    }

    public void Submit(Guid? userId, DateTimeOffset at)
    {
        EnsureStatus(AdjustmentStatus.Draft);
        if (_lines.Count == 0) throw new DomainRuleException("adjustment.empty", "Add at least one line before submitting.");
        Status = AdjustmentStatus.PendingApproval;
        SubmittedAt = at;
        SubmittedBy = userId;
    }

    /// <param name="requireDifferentApprover">When true, the person who submitted cannot approve.</param>
    public void Approve(Guid? userId, DateTimeOffset at, bool requireDifferentApprover, string? note)
    {
        EnsureStatus(AdjustmentStatus.PendingApproval);
        if (requireDifferentApprover && userId is not null && userId == SubmittedBy)
        {
            throw new DomainRuleException("adjustment.self_approval", "You cannot approve an adjustment you submitted. Ask another manager.", isConflict: true);
        }
        Status = AdjustmentStatus.Posted;
        DecidedAt = at;
        DecidedBy = userId;
        DecisionNote = Text.Optional(note, 500, "adjustment.note_invalid", "Note");
    }

    public void Reject(Guid? userId, DateTimeOffset at, string reason)
    {
        EnsureStatus(AdjustmentStatus.PendingApproval);
        Status = AdjustmentStatus.Rejected;
        DecidedAt = at;
        DecidedBy = userId;
        DecisionNote = Text.Required(reason, 500, "adjustment.reason_required", "Reason");
    }

    /// <summary>Back to draft so the requester can fix it.</summary>
    public void Reopen()
    {
        if (Status is not (AdjustmentStatus.PendingApproval or AdjustmentStatus.Rejected))
        {
            throw new DomainRuleException("adjustment.cannot_reopen", "Only submitted or rejected adjustments can be reopened.", isConflict: true);
        }
        Status = AdjustmentStatus.Draft;
        SubmittedAt = null;
        SubmittedBy = null;
        DecidedAt = null;
        DecidedBy = null;
    }

    public void Cancel()
    {
        if (Status is not (AdjustmentStatus.Draft or AdjustmentStatus.PendingApproval or AdjustmentStatus.Rejected))
        {
            throw new DomainRuleException("adjustment.cannot_cancel", "A posted adjustment cannot be cancelled. Create a new adjustment to correct it.", isConflict: true);
        }
        Status = AdjustmentStatus.Cancelled;
    }

    private void EnsureStatus(AdjustmentStatus expected)
    {
        if (Status != expected)
        {
            throw new DomainRuleException("adjustment.invalid_status",
                $"This adjustment is {Describe(Status)}; this action needs it to be {Describe(expected)}.", isConflict: true);
        }
    }

    private static string Describe(AdjustmentStatus s) => s switch
    {
        AdjustmentStatus.PendingApproval => "waiting for approval",
        _ => s.ToString().ToLowerInvariant(),
    };
}

public sealed class StockAdjustmentLine : Entity
{
    private StockAdjustmentLine() { }

    public Guid AdjustmentId { get; private set; }
    public int LineNo { get; private set; }
    public Guid ProductId { get; private set; }
    public Guid LocationId { get; private set; }
    public string? LotNo { get; private set; }
    /// <summary>Only used when stock is added to a lot that does not exist yet.</summary>
    public DateOnly? ExpiryDate { get; private set; }
    /// <summary>Signed change in base units (+ adds stock, − removes it).</summary>
    public decimal QuantityDelta { get; private set; }
    public string? Notes { get; private set; }

    public static StockAdjustmentLine Create(Guid productId, Guid locationId, string? lotNo, DateOnly? expiryDate, decimal quantityDelta, string? notes)
    {
        if (quantityDelta == 0) throw new DomainRuleException("adjustment.zero_line", "An adjustment line must add or remove stock (not zero).");
        return new StockAdjustmentLine
        {
            ProductId = productId,
            LocationId = locationId,
            LotNo = lotNo is null ? null : ProductLot.NormalizeLotNo(lotNo),
            ExpiryDate = expiryDate,
            QuantityDelta = quantityDelta,
            Notes = Text.Optional(notes, 500, "adjustment.line_notes_invalid", "Line notes"),
        };
    }

    internal void Attach(Guid adjustmentId, int lineNo)
    {
        AdjustmentId = adjustmentId;
        LineNo = lineNo;
    }
}
