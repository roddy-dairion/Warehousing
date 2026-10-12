using Inventory.Api.Infrastructure;
using Inventory.Application.Common;
using Inventory.Application.Inventory;
using Inventory.Domain.Security;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers;

/// <summary>Goods receipts: draft → post (adds stock once) → reverse.</summary>
[ApiController]
[Route("api/v1/receipts")]
[Produces("application/json")]
public sealed class ReceiptsController(ReceiptService receipts) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.InventoryRead, Permissions.InventoryReceive)]
    public Task<PagedResult<ReceiptListItemDto>> List([FromQuery] ReceiptListQuery query, CancellationToken ct) => receipts.ListAsync(query, ct);

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.InventoryRead, Permissions.InventoryReceive)]
    public Task<ReceiptDto> Get(Guid id, CancellationToken ct) => receipts.GetAsync(id, ct);

    /// <summary>Creates a draft receipt (no stock effect). Lines without a bin go to the warehouse's RECEIVING area.</summary>
    [HttpPost]
    [HasPermission(Permissions.InventoryReceive)]
    [ProducesResponseType<ReceiptDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<ReceiptDto>> Create(CreateReceiptRequest request, CancellationToken ct)
    {
        var created = await receipts.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    /// <summary>Replaces the header and lines of a draft. Send the <c>version</c> you loaded.</summary>
    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.InventoryReceive)]
    public Task<ReceiptDto> Update(Guid id, UpdateReceiptRequest request, CancellationToken ct) => receipts.UpdateAsync(id, request, ct);

    /// <summary>Posts the receipt: adds the received quantities to stock, exactly once. Accepts <c>Idempotency-Key</c>.</summary>
    [HttpPost("{id:guid}/post")]
    [HasPermission(Permissions.InventoryReceivePost)]
    [Idempotent]
    public Task<ReceiptDto> Post(Guid id, CancellationToken ct) => receipts.PostAsync(id, ct);

    /// <summary>Reverses a posted receipt (removes its stock). Fails if the stock was used or reserved. Accepts <c>Idempotency-Key</c>.</summary>
    [HttpPost("{id:guid}/reverse")]
    [HasPermission(Permissions.InventoryReceivePost)]
    [Idempotent]
    public Task<ReceiptDto> Reverse(Guid id, ReasonRequest request, CancellationToken ct) => receipts.ReverseAsync(id, request.Reason, ct);

    /// <summary>Cancels a draft.</summary>
    [HttpPost("{id:guid}/cancel")]
    [HasPermission(Permissions.InventoryReceive)]
    public Task<ReceiptDto> Cancel(Guid id, CancellationToken ct) => receipts.CancelAsync(id, ct);
}
