using Inventory.Api.Infrastructure;
using Inventory.Application.Common;
using Inventory.Application.Inventory;
using Inventory.Domain.Security;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers;

/// <summary>Stock balances, summaries, movements, expiry alerts and the reconciliation check. Every query is scoped to a warehouse the caller can access.</summary>
[ApiController]
[Route("api/v1/inventory")]
[Produces("application/json")]
[HasPermission(Permissions.InventoryRead)]
public sealed class InventoryController(InventoryQueries queries, ReconciliationService reconciliation) : ControllerBase
{
    /// <summary>Balance rows (product × bin × lot) of a warehouse, paged.</summary>
    [HttpGet("balances")]
    public Task<PagedResult<BalanceDto>> Balances([FromQuery] BalanceListQuery query, CancellationToken ct) => queries.BalancesAsync(query, ct);

    /// <summary>Per-product totals for a warehouse, with sellable stock and low-stock flags.</summary>
    [HttpGet("summary")]
    public Task<PagedResult<StockSummaryDto>> Summary([FromQuery] StockSummaryQuery query, CancellationToken ct) => queries.SummaryAsync(query, ct);

    /// <summary>One product's stock in every accessible warehouse (or one, with <c>warehouseId</c>), with the bin/lot breakdown.</summary>
    [HttpGet("products/{productId:guid}")]
    public Task<ProductStockDto> Product(Guid productId, [FromQuery] Guid? warehouseId, CancellationToken ct) => queries.ProductStockAsync(productId, warehouseId, ct);

    /// <summary>The stock ledger of a warehouse, newest first by default.</summary>
    [HttpGet("movements")]
    public Task<PagedResult<MovementDto>> Movements([FromQuery] MovementListQuery query, CancellationToken ct) => queries.MovementsAsync(query, ct);

    /// <summary>Lots with stock that expire within <c>days</c> (default from settings), including expired ones.</summary>
    [HttpGet("expiring")]
    public Task<IReadOnlyList<ExpiringLotDto>> Expiring([FromQuery] ExpiringQuery query, CancellationToken ct) => queries.ExpiringAsync(query, ct);

    /// <summary>Checks that balances match the ledger and open reservations. Read-only.</summary>
    [HttpGet("reconciliation")]
    [HasPermission(Permissions.InventoryAdjustApprove, Permissions.ReportsRead)]
    public Task<ReconciliationResult> Reconciliation([FromQuery] Guid warehouseId, CancellationToken ct) => reconciliation.CheckWarehouseAsync(warehouseId, ct);
}

/// <summary>Lots (batches) of lot-tracked products.</summary>
[ApiController]
[Produces("application/json")]
public sealed class LotsController(LotService lots) : ControllerBase
{
    /// <summary>Lots of a product with their stock in the caller's warehouses, soonest expiry first.</summary>
    [HttpGet("api/v1/products/{productId:guid}/lots")]
    [HasPermission(Permissions.InventoryRead)]
    public Task<IReadOnlyList<LotDto>> List(Guid productId, [FromQuery] bool onlyWithStock, CancellationToken ct) => lots.ListAsync(productId, onlyWithStock, ct);

    /// <summary>Releases, quarantines or blocks a lot. Quarantined and blocked lots are not reserved for dispatches.</summary>
    [HttpPost("api/v1/lots/{id:guid}/status")]
    [HasPermission(Permissions.InventoryAdjustApprove)]
    public Task<LotDto> SetStatus(Guid id, SetLotStatusRequest request, CancellationToken ct) => lots.SetStatusAsync(id, request, ct);
}
