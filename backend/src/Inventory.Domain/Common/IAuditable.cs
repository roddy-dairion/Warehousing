namespace Inventory.Domain.Common;

/// <summary>Creation/modification stamps, filled automatically when saving. All times are UTC.</summary>
public interface IAuditable
{
    DateTimeOffset CreatedAt { get; set; }
    Guid? CreatedBy { get; set; }
    DateTimeOffset UpdatedAt { get; set; }
    Guid? UpdatedBy { get; set; }
}
