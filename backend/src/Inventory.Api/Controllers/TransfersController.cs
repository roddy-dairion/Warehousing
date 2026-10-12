using Inventory.Api.Infrastructure;
using Inventory.Application.Common;
using Inventory.Application.Inventory;
using Inventory.Domain.Security;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers;

/// <summary>Stock moves. v1: bin-to-bin moves inside a warehouse (put-away). Transfers between warehouses come later.</summary>
[ApiController]
[Route("api/v1/transfers")]
[Produces("application/json")]
public sealed class TransfersController(TransferService transfers) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.InventoryRead)]
    public Task<PagedResult<TransferListItemDto>> List([FromQuery] TransferListQuery query, CancellationToken ct) => transfers.ListAsync(query, ct);

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.InventoryRead)]
    public Task<TransferDto> Get(Guid id, CancellationToken ct) => transfers.GetAsync(id, ct);

    /// <summary>Moves unreserved stock between bins in one step. Accepts <c>Idempotency-Key</c>.</summary>
    [HttpPost("bin-moves")]
    [HasPermission(Permissions.InventoryTransfer)]
    [Idempotent]
    [ProducesResponseType<TransferDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<TransferDto>> CreateBinMove(CreateBinMoveRequest request, CancellationToken ct)
    {
        var created = await transfers.CreateBinMoveAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }
}
