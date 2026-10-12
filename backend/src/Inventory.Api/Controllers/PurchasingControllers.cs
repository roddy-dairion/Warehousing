using Inventory.Api.Infrastructure;
using Inventory.Application.Common;
using Inventory.Application.Inventory;
using Inventory.Application.Purchasing;
using Inventory.Domain.Security;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers;

/// <summary>Suppliers that goods are ordered from.</summary>
[ApiController]
[Route("api/v1/suppliers")]
[Produces("application/json")]
public sealed class SuppliersController(SupplierService suppliers) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.PurchasingRead, Permissions.PurchasingManage, Permissions.ProductsManage)]
    public Task<PagedResult<SupplierListItemDto>> List([FromQuery] ListQuery query, CancellationToken ct) => suppliers.ListAsync(query, ct);

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.PurchasingRead, Permissions.PurchasingManage)]
    public Task<SupplierDto> Get(Guid id, CancellationToken ct) => suppliers.GetAsync(id, ct);

    [HttpPost]
    [HasPermission(Permissions.PurchasingManage)]
    [ProducesResponseType<SupplierDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<SupplierDto>> Create(CreateSupplierRequest request, CancellationToken ct)
    {
        var created = await suppliers.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.PurchasingManage)]
    public Task<SupplierDto> Update(Guid id, UpdateSupplierRequest request, CancellationToken ct) => suppliers.UpdateAsync(id, request, ct);

    [HttpPost("{id:guid}/activate")]
    [HasPermission(Permissions.PurchasingManage)]
    public async Task<IActionResult> Activate(Guid id, CancellationToken ct)
    {
        await suppliers.SetActiveAsync(id, true, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/deactivate")]
    [HasPermission(Permissions.PurchasingManage)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        await suppliers.SetActiveAsync(id, false, ct);
        return NoContent();
    }
}

/// <summary>Purchase orders: draft → ordered → (partially) received, or closed / cancelled. Ordering never changes stock.</summary>
[ApiController]
[Route("api/v1/purchase-orders")]
[Produces("application/json")]
public sealed class PurchaseOrdersController(PurchaseOrderService orders, ReceiptService receipts) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.PurchasingRead, Permissions.PurchasingManage)]
    public Task<PagedResult<PurchaseOrderListItemDto>> List([FromQuery] PurchaseOrderListQuery query, CancellationToken ct) => orders.ListAsync(query, ct);

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.PurchasingRead, Permissions.PurchasingManage)]
    public Task<PurchaseOrderDto> Get(Guid id, CancellationToken ct) => orders.GetAsync(id, ct);

    /// <summary>Creates a draft order. Without an expected date, the supplier's lead time proposes one.</summary>
    [HttpPost]
    [HasPermission(Permissions.PurchasingManage)]
    [ProducesResponseType<PurchaseOrderDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<PurchaseOrderDto>> Create(CreatePurchaseOrderRequest request, CancellationToken ct)
    {
        var created = await orders.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    /// <summary>Draft: everything. Placed: dates, supplier reference and notes only (send <c>lines: null</c>). Send the <c>version</c> you loaded.</summary>
    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.PurchasingManage)]
    public Task<PurchaseOrderDto> Update(Guid id, UpdatePurchaseOrderRequest request, CancellationToken ct) => orders.UpdateAsync(id, request, ct);

    /// <summary>Places the order with the supplier: it now counts as "on order".</summary>
    [HttpPost("{id:guid}/place")]
    [HasPermission(Permissions.PurchasingManage)]
    public Task<PurchaseOrderDto> Place(Guid id, CancellationToken ct) => orders.PlaceAsync(id, ct);

    /// <summary>Cancels a draft, or a placed order nothing was received against.</summary>
    [HttpPost("{id:guid}/cancel")]
    [HasPermission(Permissions.PurchasingManage)]
    public Task<PurchaseOrderDto> Cancel(Guid id, PurchaseOrderReasonRequest request, CancellationToken ct) => orders.CancelAsync(id, request.Reason, ct);

    /// <summary>Closes an open order short: the rest is no longer expected. A reason is required.</summary>
    [HttpPost("{id:guid}/close")]
    [HasPermission(Permissions.PurchasingManage)]
    public Task<PurchaseOrderDto> Close(Guid id, PurchaseOrderReasonRequest request, CancellationToken ct) => orders.CloseAsync(id, request.Reason, ct);

    /// <summary>Creates a draft goods receipt for everything still outstanding on the order (lots and bins are filled in before posting).</summary>
    [HttpPost("{id:guid}/receipts")]
    [HasPermission(Permissions.InventoryReceive)]
    [HasPermission(Permissions.PurchasingRead, Permissions.PurchasingManage)]
    [ProducesResponseType<ReceiptDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<ReceiptDto>> CreateReceipt(Guid id, CancellationToken ct)
    {
        var created = await receipts.CreateFromOrderAsync(id, ct);
        return CreatedAtAction(nameof(ReceiptsController.Get), "Receipts", new { id = created.Id }, created);
    }
}

/// <summary>Reorder suggestions and purchasing settings.</summary>
[ApiController]
[Route("api/v1/purchasing")]
[Produces("application/json")]
public sealed class PurchasingController(ReorderService reorder) : ControllerBase
{
    /// <summary>Products below their minimum stock in the warehouse, with the quantity to order.</summary>
    [HttpGet("suggestions")]
    [HasPermission(Permissions.PurchasingRead, Permissions.PurchasingManage)]
    public Task<IReadOnlyList<ReorderSuggestionDto>> Suggestions([FromQuery] Guid warehouseId, CancellationToken ct) => reorder.SuggestionsAsync(warehouseId, ct);

    [HttpGet("settings")]
    [HasPermission(Permissions.PurchasingRead, Permissions.PurchasingManage)]
    public Task<PurchasingSettingsDto> Settings(CancellationToken ct) => reorder.GetSettingsAsync(ct);

    /// <summary>Recipients of the daily low-stock email (empty = no email).</summary>
    [HttpPut("settings")]
    [HasPermission(Permissions.PurchasingManage)]
    public Task<PurchasingSettingsDto> UpdateSettings(UpdatePurchasingSettingsRequest request, CancellationToken ct) => reorder.UpdateSettingsAsync(request, ct);
}
