using Inventory.Api.Infrastructure;
using Inventory.Application.Common;
using Inventory.Application.Warehouses;
using Inventory.Domain.Security;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers;

/// <summary>Warehouses (branches) and their bins.</summary>
[ApiController]
[Route("api/v1/warehouses")]
[Produces("application/json")]
public sealed class WarehousesController(WarehouseService warehouses, LocationService locations) : ControllerBase
{
    /// <summary>Warehouses the caller may act in. Administrators can add <c>includeInactive=true</c>.</summary>
    [HttpGet]
    public Task<IReadOnlyList<WarehouseDto>> List([FromQuery] bool includeInactive, CancellationToken ct) => warehouses.ListAsync(includeInactive, ct);

    /// <summary>One warehouse. 404 when it does not exist or is outside the caller's access.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<WarehouseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<WarehouseDto> Get(Guid id, CancellationToken ct) => warehouses.GetAsync(id, ct);

    [HttpPost]
    [HasPermission(Permissions.WarehousesManage)]
    [ProducesResponseType<WarehouseDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<WarehouseDto>> Create(CreateWarehouseRequest request, CancellationToken ct)
    {
        var created = await warehouses.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.WarehousesManage)]
    public Task<WarehouseDto> Update(Guid id, UpdateWarehouseRequest request, CancellationToken ct) => warehouses.UpdateAsync(id, request, ct);

    [HttpPost("{id:guid}/activate")]
    [HasPermission(Permissions.WarehousesManage)]
    public async Task<IActionResult> Activate(Guid id, CancellationToken ct)
    {
        await warehouses.SetActiveAsync(id, true, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/deactivate")]
    [HasPermission(Permissions.WarehousesManage)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        await warehouses.SetActiveAsync(id, false, ct);
        return NoContent();
    }

    // ---- Bins -----------------------------------------------------------------------------

    /// <summary>Bins and areas of a warehouse, paged.</summary>
    [HttpGet("{warehouseId:guid}/locations")]
    [HasPermission(Permissions.InventoryRead, Permissions.WarehousesManage)]
    public Task<PagedResult<LocationDto>> ListLocations(Guid warehouseId, [FromQuery] LocationListQuery query, CancellationToken ct) =>
        locations.ListAsync(warehouseId, query, ct);

    [HttpPost("{warehouseId:guid}/locations")]
    [HasPermission(Permissions.WarehousesManage)]
    [ProducesResponseType<LocationDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<LocationDto>> CreateLocation(Guid warehouseId, CreateLocationRequest request, CancellationToken ct)
    {
        var created = await locations.CreateAsync(warehouseId, request, ct);
        return CreatedAtAction(nameof(LocationsController.Get), "Locations", new { id = created.Id }, created);
    }
}

/// <summary>Single bin operations.</summary>
[ApiController]
[Route("api/v1/locations")]
[Produces("application/json")]
public sealed class LocationsController(LocationService locations) : ControllerBase
{
    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.InventoryRead, Permissions.WarehousesManage)]
    public Task<LocationDto> Get(Guid id, CancellationToken ct) => locations.GetAsync(id, ct);

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.WarehousesManage)]
    public Task<LocationDto> Update(Guid id, UpdateLocationRequest request, CancellationToken ct) => locations.UpdateAsync(id, request, ct);

    [HttpPost("{id:guid}/activate")]
    [HasPermission(Permissions.WarehousesManage)]
    public async Task<IActionResult> Activate(Guid id, CancellationToken ct)
    {
        await locations.SetActiveAsync(id, true, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/deactivate")]
    [HasPermission(Permissions.WarehousesManage)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        await locations.SetActiveAsync(id, false, ct);
        return NoContent();
    }
}
