using Inventory.Api.Infrastructure;
using Inventory.Application.Products;
using Inventory.Domain.Security;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers;

[ApiController]
[Route("api/v1")]
[Produces("application/json")]
public sealed class CatalogController(CatalogService catalog) : ControllerBase
{
    [HttpGet("categories")]
    [HasPermission(Permissions.ProductsRead)]
    public Task<IReadOnlyList<CategoryDto>> ListCategories(CancellationToken ct) => catalog.ListCategoriesAsync(ct);

    [HttpPost("categories")]
    [HasPermission(Permissions.ProductsManage)]
    public Task<CategoryDto> CreateCategory(CreateCategoryRequest request, CancellationToken ct) => catalog.CreateCategoryAsync(request, ct);

    [HttpPut("categories/{id:guid}")]
    [HasPermission(Permissions.ProductsManage)]
    public Task<CategoryDto> UpdateCategory(Guid id, UpdateCategoryRequest request, CancellationToken ct) => catalog.UpdateCategoryAsync(id, request, ct);

    [HttpPost("categories/{id:guid}/activate")]
    [HasPermission(Permissions.ProductsManage)]
    public async Task<IActionResult> ActivateCategory(Guid id, CancellationToken ct)
    {
        await catalog.SetCategoryActiveAsync(id, true, ct);
        return NoContent();
    }

    [HttpPost("categories/{id:guid}/deactivate")]
    [HasPermission(Permissions.ProductsManage)]
    public async Task<IActionResult> DeactivateCategory(Guid id, CancellationToken ct)
    {
        await catalog.SetCategoryActiveAsync(id, false, ct);
        return NoContent();
    }

    [HttpGet("units")]
    [HasPermission(Permissions.ProductsRead)]
    public Task<List<UnitDto>> ListUnits(CancellationToken ct) => catalog.ListUnitsAsync(ct);

    [HttpPost("units")]
    [HasPermission(Permissions.ProductsManage)]
    public Task<UnitDto> CreateUnit(CreateUnitRequest request, CancellationToken ct) => catalog.CreateUnitAsync(request, ct);

    [HttpPut("units/{id:guid}")]
    [HasPermission(Permissions.ProductsManage)]
    public Task<UnitDto> UpdateUnit(Guid id, UpdateUnitRequest request, CancellationToken ct) => catalog.UpdateUnitAsync(id, request, ct);

    [HttpPost("units/{id:guid}/activate")]
    [HasPermission(Permissions.ProductsManage)]
    public async Task<IActionResult> ActivateUnit(Guid id, CancellationToken ct)
    {
        await catalog.SetUnitActiveAsync(id, true, ct);
        return NoContent();
    }

    [HttpPost("units/{id:guid}/deactivate")]
    [HasPermission(Permissions.ProductsManage)]
    public async Task<IActionResult> DeactivateUnit(Guid id, CancellationToken ct)
    {
        await catalog.SetUnitActiveAsync(id, false, ct);
        return NoContent();
    }
}
