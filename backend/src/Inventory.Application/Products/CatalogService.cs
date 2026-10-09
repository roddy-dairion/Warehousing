using Inventory.Application.Common;
using Inventory.Domain.Common;
using Inventory.Domain.Products;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Application.Products;

/// <summary>Product categories and units of measure (small lists, returned whole).</summary>
public sealed class CatalogService(IApplicationDbContext db, IAuditWriter audit)
{
    // ---- Categories ------------------------------------------------------------------------

    public async Task<IReadOnlyList<CategoryDto>> ListCategoriesAsync(CancellationToken cancellationToken)
    {
        var rows = await db.ProductCategories.AsNoTracking()
            .Select(c => new
            {
                c.Id, c.Code, c.Name, c.ParentId, c.IsActive,
                Count = db.Products.Count(p => p.CategoryId == c.Id),
                Version = EF.Property<uint>(c, Versioning.Column),
            })
            .ToListAsync(cancellationToken);
        var byId = rows.ToDictionary(r => r.Id);
        string PathOf(Guid id)
        {
            var parts = new List<string>();
            for (Guid? cur = id; cur is not null && byId.TryGetValue(cur.Value, out var r) && parts.Count < 10; cur = r.ParentId) parts.Insert(0, r.Name);
            return string.Join(" › ", parts);
        }
        return rows.Select(r => new CategoryDto(r.Id, r.Code, r.Name, r.ParentId, PathOf(r.Id), r.IsActive, r.Count, r.Version))
            .OrderBy(c => c.Path, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<CategoryDto> CreateCategoryAsync(CreateCategoryRequest r, CancellationToken cancellationToken)
    {
        var category = ProductCategory.Create(r.Code, r.Name, r.ParentId);
        if (await db.ProductCategories.AnyAsync(c => c.Code == category.Code, cancellationToken))
        {
            throw new DomainRuleException("category.code_taken", $"Category code {category.Code} is already used.", isConflict: true);
        }
        await EnsureParentValidAsync(category.Id, r.ParentId, cancellationToken);
        db.ProductCategories.Add(category);
        audit.Add("category.created", "category", category.Id.ToString(), data: new { category.Code, category.Name });
        await db.SaveChangesAsync(cancellationToken);
        return (await ListCategoriesAsync(cancellationToken)).Single(c => c.Id == category.Id);
    }

    public async Task<CategoryDto> UpdateCategoryAsync(Guid id, UpdateCategoryRequest r, CancellationToken cancellationToken)
    {
        var category = await db.ProductCategories.SingleOrDefaultAsync(c => c.Id == id, cancellationToken) ?? throw new NotFoundException("Category not found.");
        db.ExpectVersion(category, r.Version);
        await EnsureParentValidAsync(id, r.ParentId, cancellationToken);
        category.Update(r.Name, r.ParentId);
        audit.Add("category.updated", "category", id.ToString(), data: new { category.Name, category.ParentId });
        await db.SaveChangesAsync(cancellationToken);
        return (await ListCategoriesAsync(cancellationToken)).Single(c => c.Id == id);
    }

    public async Task SetCategoryActiveAsync(Guid id, bool active, CancellationToken cancellationToken)
    {
        var category = await db.ProductCategories.SingleOrDefaultAsync(c => c.Id == id, cancellationToken) ?? throw new NotFoundException("Category not found.");
        if (active) category.Activate(); else category.Deactivate();
        audit.Add(active ? "category.activated" : "category.deactivated", "category", id.ToString());
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureParentValidAsync(Guid id, Guid? parentId, CancellationToken cancellationToken)
    {
        if (parentId is null) return;
        var parents = await db.ProductCategories.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.ParentId, cancellationToken);
        if (!parents.ContainsKey(parentId.Value)) throw new DomainRuleException("category.parent_missing", "The parent category does not exist.");
        for (Guid? cur = parentId; cur is not null; cur = parents.GetValueOrDefault(cur.Value))
        {
            if (cur == id) throw new DomainRuleException("category.cycle", "A category cannot be placed under one of its own sub-categories.");
        }
    }

    // ---- Units -------------------------------------------------------------------------------

    public Task<List<UnitDto>> ListUnitsAsync(CancellationToken cancellationToken) =>
        db.UnitsOfMeasure.AsNoTracking().OrderBy(u => u.Code)
            .Select(u => new UnitDto(u.Id, u.Code, u.Name, u.Decimals, u.IsActive, EF.Property<uint>(u, Versioning.Column)))
            .ToListAsync(cancellationToken);

    public async Task<UnitDto> CreateUnitAsync(CreateUnitRequest r, CancellationToken cancellationToken)
    {
        var unit = UnitOfMeasure.Create(r.Code, r.Name, r.Decimals);
        if (await db.UnitsOfMeasure.AnyAsync(u => u.Code == unit.Code, cancellationToken))
        {
            throw new DomainRuleException("unit.code_taken", $"Unit {unit.Code} already exists.", isConflict: true);
        }
        db.UnitsOfMeasure.Add(unit);
        audit.Add("unit.created", "unit", unit.Id.ToString(), data: new { unit.Code, unit.Decimals });
        await db.SaveChangesAsync(cancellationToken);
        return (await ListUnitsAsync(cancellationToken)).Single(u => u.Id == unit.Id);
    }

    public async Task<UnitDto> UpdateUnitAsync(Guid id, UpdateUnitRequest r, CancellationToken cancellationToken)
    {
        var unit = await db.UnitsOfMeasure.SingleOrDefaultAsync(u => u.Id == id, cancellationToken) ?? throw new NotFoundException("Unit not found.");
        db.ExpectVersion(unit, r.Version);
        unit.Update(r.Name, r.Decimals);
        audit.Add("unit.updated", "unit", id.ToString(), data: new { unit.Name, unit.Decimals });
        await db.SaveChangesAsync(cancellationToken);
        return (await ListUnitsAsync(cancellationToken)).Single(u => u.Id == id);
    }

    public async Task SetUnitActiveAsync(Guid id, bool active, CancellationToken cancellationToken)
    {
        var unit = await db.UnitsOfMeasure.SingleOrDefaultAsync(u => u.Id == id, cancellationToken) ?? throw new NotFoundException("Unit not found.");
        if (!active && await db.Products.AnyAsync(p => p.IsActive && p.BaseUnitId == id, cancellationToken))
        {
            throw new DomainRuleException("unit.in_use", "This unit is the base unit of active products and cannot be deactivated.", isConflict: true);
        }
        if (active) unit.Activate(); else unit.Deactivate();
        audit.Add(active ? "unit.activated" : "unit.deactivated", "unit", id.ToString());
        await db.SaveChangesAsync(cancellationToken);
    }
}
