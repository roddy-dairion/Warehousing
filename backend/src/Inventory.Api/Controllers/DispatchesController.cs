using Inventory.Api.Infrastructure;
using Inventory.Application.Common;
using Inventory.Application.Dispatches;
using Inventory.Application.Documents;
using Inventory.Domain.Documents;
using Inventory.Domain.Security;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers;

/// <summary>
/// Dispatches: draft → submit → approve (reserves stock) → picking → ready → ship (deducts stock) → deliver (proof of delivery).
/// Status changes only through these commands; the API enforces the allowed transitions.
/// </summary>
[ApiController]
[Route("api/v1/dispatches")]
[Produces("application/json")]
public sealed class DispatchesController(DispatchService dispatches, PickingService picking, DeliveryService delivery, DocumentService documents) : ControllerBase
{
    private const long MaxDeliveryUpload = 120L * 1024 * 1024;

    [HttpGet]
    [HasPermission(Permissions.DispatchesRead)]
    public Task<PagedResult<DispatchListItemDto>> List([FromQuery] DispatchListQuery query, CancellationToken ct) => dispatches.ListAsync(query, ct);

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.DispatchesRead)]
    public Task<DispatchDto> Get(Guid id, CancellationToken ct) => dispatches.GetAsync(id, ct);

    /// <summary>Creates a draft (no stock effect).</summary>
    [HttpPost]
    [HasPermission(Permissions.DispatchesCreate)]
    [ProducesResponseType<DispatchDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<DispatchDto>> Create(CreateDispatchRequest request, CancellationToken ct)
    {
        var created = await dispatches.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    /// <summary>Replaces the header and lines of a draft. Send the <c>version</c> you loaded.</summary>
    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.DispatchesCreate)]
    public Task<DispatchDto> Update(Guid id, UpdateDispatchRequest request, CancellationToken ct) => dispatches.UpdateAsync(id, request, ct);

    [HttpPost("{id:guid}/submit")]
    [HasPermission(Permissions.DispatchesCreate)]
    public Task<DispatchDto> Submit(Guid id, CancellationToken ct) => dispatches.SubmitAsync(id, ct);

    /// <summary>Sends a submitted dispatch back to draft, with a reason.</summary>
    [HttpPost("{id:guid}/return")]
    [HasPermission(Permissions.DispatchesApprove)]
    public Task<DispatchDto> Return(Guid id, DispatchReasonRequest request, CancellationToken ct) => dispatches.ReturnAsync(id, request.Reason, ct);

    /// <summary>Approves and reserves stock for every line (all or nothing; 409 <c>stock.shortage</c> lists shortages). Accepts <c>Idempotency-Key</c>.</summary>
    [HttpPost("{id:guid}/approve")]
    [HasPermission(Permissions.DispatchesApprove)]
    [Idempotent]
    public Task<DispatchDto> Approve(Guid id, DispatchNoteRequest request, CancellationToken ct) => dispatches.ApproveAsync(id, request.Note, ct);

    /// <summary>Starts picking, optionally assigning an operator.</summary>
    [HttpPost("{id:guid}/picking")]
    [HasPermission(Permissions.DispatchesPick)]
    public Task<DispatchDto> StartPicking(Guid id, StartPickingRequest request, CancellationToken ct) => dispatches.StartPickingAsync(id, request.AssignedToId, ct);

    /// <summary>Assigns (or unassigns) the picker.</summary>
    [HttpPost("{id:guid}/assign")]
    [HasPermission(Permissions.DispatchesApprove)]
    public Task<DispatchDto> Assign(Guid id, AssignRequest request, CancellationToken ct) => dispatches.AssignAsync(id, request.AssignedToId, ct);

    /// <summary>Allocations to pick, in bin order.</summary>
    [HttpGet("{id:guid}/pick-list")]
    [HasPermission(Permissions.DispatchesPick, Permissions.DispatchesRead)]
    public Task<PickListDto> PickList(Guid id, CancellationToken ct) => picking.PickListAsync(id, ct);

    /// <summary>Records a scan (bin + product/pack barcode + lot + quantity). The same <c>scanId</c> is never counted twice.</summary>
    [HttpPost("{id:guid}/picks")]
    [HasPermission(Permissions.DispatchesPick)]
    public Task<PickResultDto> Pick(Guid id, PickRequest request, CancellationToken ct) => picking.PickAsync(id, request, ct);

    [HttpPost("{id:guid}/picks/{pickId:guid}/undo")]
    [HasPermission(Permissions.DispatchesPick)]
    public Task<PickResultDto> UndoPick(Guid id, Guid pickId, UndoPickRequest request, CancellationToken ct) => picking.UndoAsync(id, pickId, request.ScanId, ct);

    /// <summary>Takes (part of) an allocation from another bin or lot.</summary>
    [HttpPost("{id:guid}/reallocate")]
    [HasPermission(Permissions.DispatchesPick)]
    public Task<PickListDto> Reallocate(Guid id, ReallocateRequest request, CancellationToken ct) => picking.ReallocateAsync(id, request, ct);

    /// <summary>Reports missing, damaged, unexpected or wrong-lot goods.</summary>
    [HttpPost("{id:guid}/discrepancies")]
    [HasPermission(Permissions.DispatchesPick)]
    public Task<DiscrepancyDto> ReportDiscrepancy(Guid id, DiscrepancyRequest request, CancellationToken ct) => picking.ReportDiscrepancyAsync(id, request, ct);

    /// <summary>Picking done. Short lines need <c>allowShort: true</c>.</summary>
    [HttpPost("{id:guid}/ready")]
    [HasPermission(Permissions.DispatchesPick)]
    public Task<DispatchDto> Ready(Guid id, CompletePickingRequest request, CancellationToken ct) => dispatches.CompletePickingAsync(id, request.AllowShort, ct);

    [HttpPost("{id:guid}/reopen-picking")]
    [HasPermission(Permissions.DispatchesApprove)]
    public Task<DispatchDto> ReopenPicking(Guid id, DispatchNoteRequest request, CancellationToken ct) => dispatches.ReopenPickingAsync(id, request.Note, ct);

    /// <summary>
    /// Confirms shipment: deducts the picked quantities from stock, exactly once. Short lines need <c>shortDecision</c>
    /// (closeShort or backorder). Produces the packing slip. Accepts <c>Idempotency-Key</c>.
    /// </summary>
    [HttpPost("{id:guid}/ship")]
    [HasPermission(Permissions.DispatchesShip)]
    [Idempotent]
    public Task<DispatchDto> Ship(Guid id, ShipRequest request, CancellationToken ct) => dispatches.ShipAsync(id, request, ct);

    /// <summary>
    /// Records proof of delivery (multipart form): <c>receiverName</c>, <c>signature</c> (image), one or more <c>photos</c>,
    /// optional <c>deliveredAt</c> and <c>notes</c>. No stock effect. The signed delivery note is then emailed to the customer.
    /// </summary>
    [HttpPost("{id:guid}/deliver")]
    [HasPermission(Permissions.DispatchesDeliver)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxDeliveryUpload)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxDeliveryUpload)]
    public Task<DispatchDto> Deliver(Guid id, [FromForm] string? receiverName, [FromForm] DateTimeOffset? deliveredAt, [FromForm] string? notes,
        IFormFile? signature, [FromForm] List<IFormFile>? photos, CancellationToken ct)
    {
        static UploadedFile Wrap(IFormFile f) => new(f.FileName, f.Length, f.OpenReadStream);
        var command = new DeliverCommand(receiverName ?? string.Empty, deliveredAt, notes, signature is null ? null : Wrap(signature), (photos ?? []).Select(Wrap).ToList());
        return delivery.DeliverAsync(id, command, ct);
    }

    /// <summary>Delivery signature or photo.</summary>
    [HttpGet("{id:guid}/pod/files/{fileId:guid}")]
    [HasPermission(Permissions.DispatchesRead)]
    public async Task<IActionResult> PodFile(Guid id, Guid fileId, CancellationToken ct)
    {
        var (content, contentType, _) = await delivery.GetPodFileAsync(id, fileId, ct);
        Response.Headers.CacheControl = "private, max-age=3600";
        return File(content, contentType);
    }

    /// <summary>Downloads a generated PDF (packing slip or delivery note).</summary>
    [HttpGet("{id:guid}/documents/{documentId:guid}")]
    [HasPermission(Permissions.DispatchesRead)]
    public async Task<IActionResult> Document(Guid id, Guid documentId, CancellationToken ct)
    {
        var (content, fileName) = await documents.OpenAsync(id, documentId, ct);
        return File(content, "application/pdf", fileName);
    }

    /// <summary>Generates a document again (e.g. after a template change).</summary>
    [HttpPost("{id:guid}/documents/{type}")]
    [HasPermission(Permissions.DispatchesShip, Permissions.DispatchesDeliver)]
    public async Task<DispatchDto> Regenerate(Guid id, DocumentType type, CancellationToken ct)
    {
        await documents.RegenerateAsync(id, type, ct);
        return await dispatches.GetAsync(id, ct);
    }

    /// <summary>Emails the delivery note again (to the customer, or to <c>to</c>).</summary>
    [HttpPost("{id:guid}/delivery-note/send")]
    [HasPermission(Permissions.DispatchesDeliver)]
    public Task<DispatchDto> SendDeliveryNote(Guid id, SendDeliveryNoteRequest request, CancellationToken ct) => delivery.SendDeliveryNoteAsync(id, request.To, ct);

    /// <summary>Cancels before shipment and releases reserved stock. A reason is required.</summary>
    [HttpPost("{id:guid}/cancel")]
    [HasPermission(Permissions.DispatchesCancel)]
    [Idempotent]
    public Task<DispatchDto> Cancel(Guid id, DispatchReasonRequest request, CancellationToken ct) => dispatches.CancelAsync(id, request.Reason, ct);
}

/// <summary>Company details and document templates (PDF layouts).</summary>
[ApiController]
[Produces("application/json")]
public sealed class DocumentSettingsController(TemplateService templates) : ControllerBase
{
    [HttpGet("api/v1/settings/company")]
    [HasPermission(Permissions.SettingsManage)]
    public Task<CompanyProfileDto> GetCompany(CancellationToken ct) => templates.GetCompanyAsync(ct);

    [HttpPut("api/v1/settings/company")]
    [HasPermission(Permissions.SettingsManage)]
    public Task<CompanyProfileDto> UpdateCompany(UpdateCompanyProfileRequest request, CancellationToken ct) => templates.UpdateCompanyAsync(request, ct);

    [HttpGet("api/v1/settings/company/logo")]
    [HasPermission(Permissions.SettingsManage)]
    public async Task<IActionResult> GetLogo(CancellationToken ct)
    {
        var (content, contentType) = await templates.GetLogoAsync(ct);
        return File(content, contentType);
    }

    [HttpPost("api/v1/settings/company/logo")]
    [HasPermission(Permissions.SettingsManage)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(2 * 1024 * 1024)]
    public async Task<IActionResult> SetLogo(IFormFile file, CancellationToken ct)
    {
        await using var stream = file.OpenReadStream();
        await templates.SetLogoAsync(file.FileName, stream, file.Length, ct);
        return NoContent();
    }

    [HttpDelete("api/v1/settings/company/logo")]
    [HasPermission(Permissions.SettingsManage)]
    public async Task<IActionResult> RemoveLogo(CancellationToken ct)
    {
        await templates.RemoveLogoAsync(ct);
        return NoContent();
    }

    /// <summary>Current template of each document type, with version history and the available placeholders.</summary>
    [HttpGet("api/v1/document-templates")]
    [HasPermission(Permissions.SettingsManage)]
    public Task<IReadOnlyList<TemplateDto>> ListTemplates(CancellationToken ct) => templates.ListAsync(ct);

    [HttpGet("api/v1/document-templates/{type}")]
    [HasPermission(Permissions.SettingsManage)]
    public Task<TemplateDto> GetTemplate(DocumentType type, [FromQuery] int? version, CancellationToken ct) => templates.GetAsync(type, version, ct);

    /// <summary>Saves a new version (checked against sample data first; errors return 422).</summary>
    [HttpPut("api/v1/document-templates/{type}")]
    [HasPermission(Permissions.SettingsManage)]
    public Task<TemplateDto> SaveTemplate(DocumentType type, SaveTemplateRequest request, CancellationToken ct) => templates.SaveAsync(type, request, ct);

    [HttpPost("api/v1/document-templates/{type}/versions/{version:int}/restore")]
    [HasPermission(Permissions.SettingsManage)]
    public Task<TemplateDto> RestoreTemplate(DocumentType type, int version, CancellationToken ct) => templates.RestoreAsync(type, version, ct);

    /// <summary>PDF preview of the given HTML (or the current template) with sample data or a real dispatch.</summary>
    [HttpPost("api/v1/document-templates/{type}/preview")]
    [HasPermission(Permissions.SettingsManage)]
    [Produces("application/pdf")]
    public async Task<IActionResult> Preview(DocumentType type, PreviewTemplateRequest request, CancellationToken ct) =>
        File(await templates.PreviewAsync(type, request, ct), "application/pdf");
}
