using System.Text.RegularExpressions;
using Inventory.Domain.Common;

namespace Inventory.Domain.Products;

public sealed partial class ProductCategory : Entity, IAuditable
{
    private ProductCategory() { }

    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public Guid? ParentId { get; private set; }
    public bool IsActive { get; private set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public static ProductCategory Create(string code, string name, Guid? parentId)
    {
        var c = new ProductCategory
        {
            Code = Text.Code(code, CodePattern(), "category.code_invalid", "Category code must be 1–32 letters, digits or hyphens."),
        };
        c.Update(name, parentId);
        return c;
    }

    /// <remarks>Cycle detection across several levels is done by the application service (it needs the other categories).</remarks>
    public void Update(string name, Guid? parentId)
    {
        if (parentId == Id) throw new DomainRuleException("category.parent_self", "A category cannot be its own parent.");
        Name = Text.Required(name, 120, "category.name_invalid", "Category name");
        ParentId = parentId;
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;

    [GeneratedRegex("^[A-Z0-9][A-Z0-9-]{0,31}$")]
    private static partial Regex CodePattern();
}

/// <summary>Unit of measure (EA, KG, CTN...). <see cref="Decimals"/> controls the quantity precision allowed.</summary>
public sealed partial class UnitOfMeasure : Entity, IAuditable
{
    public const int MaxDecimals = 3;

    private UnitOfMeasure() { }

    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public int Decimals { get; private set; }
    public bool IsActive { get; private set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public static UnitOfMeasure Create(string code, string name, int decimals)
    {
        var u = new UnitOfMeasure
        {
            Code = Text.Code(code, CodePattern(), "unit.code_invalid", "Unit code must be 1–10 letters or digits."),
        };
        u.Update(name, decimals);
        return u;
    }

    public void Update(string name, int decimals)
    {
        if (decimals is < 0 or > MaxDecimals)
        {
            throw new DomainRuleException("unit.decimals_invalid", $"Decimals must be between 0 and {MaxDecimals}.");
        }
        Name = Text.Required(name, 60, "unit.name_invalid", "Unit name");
        Decimals = decimals;
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;

    [GeneratedRegex("^[A-Z0-9]{1,10}$")]
    private static partial Regex CodePattern();
}
