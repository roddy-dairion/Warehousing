using Inventory.Application.Warehouses;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers;

/// <summary>Warehouses visible to the caller. Create/edit arrives in Phase 3.</summary>
[ApiController]
[Route("api/v1/warehouses")]
[Produces("application/json")]
public sealed class WarehousesController(WarehouseQueries queries) : ControllerBase
{
    /// <summary>Active warehouses the caller may act in.</summary>
    [HttpGet]
    public Task<IReadOnlyList<WarehouseDto>> List(CancellationToken cancellationToken) => queries.ListAccessibleAsync(cancellationToken);

    /// <summary>One warehouse. Returns 404 when it does not exist or is outside the caller's access.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<WarehouseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<WarehouseDto> Get(Guid id, CancellationToken cancellationToken) => queries.GetAsync(id, cancellationToken);
}
