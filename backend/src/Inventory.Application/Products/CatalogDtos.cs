namespace Inventory.Application.Products;

public sealed record CategoryDto(Guid Id, string Code, string Name, Guid? ParentId, string Path, bool IsActive, int ProductCount, uint Version);

public sealed record CreateCategoryRequest(string Code, string Name, Guid? ParentId);

public sealed record UpdateCategoryRequest(string Name, Guid? ParentId, uint Version);

public sealed record UnitDto(Guid Id, string Code, string Name, int Decimals, bool IsActive, uint Version);

public sealed record CreateUnitRequest(string Code, string Name, int Decimals);

public sealed record UpdateUnitRequest(string Name, int Decimals, uint Version);
