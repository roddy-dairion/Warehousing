using Inventory.Api.Infrastructure;
using Inventory.Application.Common;
using Inventory.Application.Inventory;
using Inventory.Domain.Security;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers;

/// <summary>Stock adjustments: draft → submit → approve (posts the stock change) or reject.</summary>
[ApiController]
[Route("api/v1/adjustments")]
[Produces("application/json")]
public sealed class AdjustmentsController(AdjustmentService adjustments) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.InventoryAdjust, Permissions.InventoryAdjustApprove)]
    public Task<PagedResult<AdjustmentListItemDto>> List([FromQuery] AdjustmentListQuery query, CancellationToken ct) => adjustments.ListAsync(query, ct);

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.InventoryAdjust, Permissions.InventoryAdjustApprove)]
    public Task<AdjustmentDto> Get(Guid id, CancellationToken ct) => adjustments.GetAsync(id, ct);

    [HttpPost]
    [HasPermission(Permissions.InventoryAdjust)]
    [ProducesResponseType<AdjustmentDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<AdjustmentDto>> Create(CreateAdjustmentRequest request, CancellationToken ct)
    {
        var created = await adjustments.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.InventoryAdjust)]
    public Task<AdjustmentDto> Update(Guid id, UpdateAdjustmentRequest request, CancellationToken ct) => adjustments.UpdateAsync(id, request, ct);

    /// <summary>Sends the draft for approval.</summary>
    [HttpPost("{id:guid}/submit")]
    [HasPermission(Permissions.InventoryAdjust)]
    public Task<AdjustmentDto> Submit(Guid id, CancellationToken ct) => adjustments.SubmitAsync(id, ct);

    /// <summary>Approves and posts the stock change. The submitter cannot approve (configurable). Accepts <c>Idempotency-Key</c>.</summary>
    [HttpPost("{id:guid}/approve")]
    [HasPermission(Permissions.InventoryAdjustApprove)]
    [Idempotent]
    public Task<AdjustmentDto> Approve(Guid id, ApproveRequest request, CancellationToken ct) => adjustments.ApproveAsync(id, request.Note, ct);

    [HttpPost("{id:guid}/reject")]
    [HasPermission(Permissions.InventoryAdjustApprove)]
    public Task<AdjustmentDto> Reject(Guid id, ReasonRequest request, CancellationToken ct) => adjustments.RejectAsync(id, request.Reason, ct);

    /// <summary>Returns a submitted or rejected adjustment to draft for editing.</summary>
    [HttpPost("{id:guid}/reopen")]
    [HasPermission(Permissions.InventoryAdjust)]
    public Task<AdjustmentDto> Reopen(Guid id, CancellationToken ct) => adjustments.ReopenAsync(id, ct);

    [HttpPost("{id:guid}/cancel")]
    [HasPermission(Permissions.InventoryAdjust)]
    public Task<AdjustmentDto> Cancel(Guid id, CancellationToken ct) => adjustments.CancelAsync(id, ct);
}
