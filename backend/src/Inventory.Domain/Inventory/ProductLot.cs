using Inventory.Domain.Common;

namespace Inventory.Domain.Inventory;

public enum LotStatus
{
    /// <summary>Usable: can be reserved and dispatched.</summary>
    Released,
    /// <summary>On hold pending inspection; not reservable.</summary>
    Quarantine,
    /// <summary>Must not be used (recall, damage); not reservable.</summary>
    Blocked,
}

/// <summary>A batch of a lot-tracked product. Expiry is recorded here, once per lot.</summary>
public sealed class ProductLot : Entity, IAuditable
{
    public const int LotNoMaxLength = 40;

    private ProductLot() { }

    public Guid ProductId { get; private set; }
    public string LotNo { get; private set; } = null!;
    public DateOnly? ExpiryDate { get; private set; }
    public LotStatus Status { get; private set; } = LotStatus.Released;

    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public static ProductLot Create(Guid productId, string lotNo, DateOnly? expiryDate) => new()
    {
        ProductId = productId,
        LotNo = NormalizeLotNo(lotNo),
        ExpiryDate = expiryDate,
    };

    public static string NormalizeLotNo(string? lotNo)
    {
        var v = (lotNo ?? string.Empty).Trim().ToUpperInvariant();
        if (v.Length == 0 || v.Length > LotNoMaxLength || v.Any(char.IsWhiteSpace))
        {
            throw new DomainRuleException("lot.number_invalid", $"Lot number must be 1–{LotNoMaxLength} characters without spaces.");
        }
        return v;
    }

    public bool IsReservable(DateOnly onDate) => Status == LotStatus.Released && (ExpiryDate is null || ExpiryDate >= onDate);

    public void SetStatus(LotStatus status) => Status = status;

    /// <summary>The first receipt sets the expiry; later receipts of the same lot must agree.</summary>
    public void ConfirmExpiry(DateOnly? expiryDate)
    {
        if (expiryDate is null) return;
        if (ExpiryDate is null)
        {
            ExpiryDate = expiryDate;
            return;
        }
        if (ExpiryDate != expiryDate)
        {
            throw new DomainRuleException("lot.expiry_mismatch", $"Lot {LotNo} is already recorded with expiry {ExpiryDate:yyyy-MM-dd}.");
        }
    }
}
