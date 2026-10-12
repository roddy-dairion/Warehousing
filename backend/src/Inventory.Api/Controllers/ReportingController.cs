using Inventory.Api.Infrastructure;
using Inventory.Application.Common;
using Inventory.Application.Reporting;
using Inventory.Domain.Dispatches;
using Inventory.Domain.Security;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers;

/// <summary>Dashboard figures, reports (table preview and CSV) and the audit log.</summary>
[ApiController]
[Produces("application/json")]
public sealed class ReportingController(DashboardService dashboard, ReportService reports, AuditQueries audit) : ControllerBase
{
    /// <summary>Figures for the warehouse dashboard; sections the caller has no permission for are null.</summary>
    [HttpGet("api/v1/dashboard/warehouse")]
    public Task<WarehouseDashboardDto> Warehouse([FromQuery] Guid warehouseId, CancellationToken ct) => dashboard.WarehouseAsync(warehouseId, ct);

    [HttpGet("api/v1/reports")]
    [HasPermission(Permissions.ReportsRead)]
    public IReadOnlyList<ReportInfoDto> Reports() => ReportKinds.All;

    /// <summary>Report rows for a table preview (first 500 rows; <c>truncated</c> tells when there are more).</summary>
    [HttpGet("api/v1/reports/{kind}")]
    [HasPermission(Permissions.ReportsRead)]
    public Task<ReportResult> Report(string kind, [FromQuery] Guid warehouseId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] DispatchStatus? status, CancellationToken ct) =>
        reports.RunAsync(kind, new ReportQuery(warehouseId, from, to, status), ReportService.PreviewRows, ct);

    /// <summary>The full report as CSV (UTF-8, up to 50 000 rows).</summary>
    [HttpGet("api/v1/reports/{kind}/csv")]
    [HasPermission(Permissions.ReportsRead)]
    [Produces("text/csv")]
    public async Task<IActionResult> ReportCsv(string kind, [FromQuery] Guid warehouseId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] DispatchStatus? status, CancellationToken ct)
    {
        var result = await reports.RunAsync(kind, new ReportQuery(warehouseId, from, to, status), ReportService.MaxRows, ct);
        return File(ReportService.ToCsv(result), "text/csv; charset=utf-8", $"{kind}-{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    [HttpGet("api/v1/audit-logs")]
    [HasPermission(Permissions.AuditRead)]
    public Task<PagedResult<AuditEntryDto>> Audit([FromQuery] AuditListQuery query, CancellationToken ct) => audit.ListAsync(query, ct);
}
