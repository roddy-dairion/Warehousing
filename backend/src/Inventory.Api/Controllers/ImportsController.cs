using System.Text;
using Inventory.Application.Common;
using Inventory.Application.Imports;
using Inventory.Domain.Security;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers;

/// <summary>CSV import of products, customers and bins. Validate with <c>dryRun=true</c> first.</summary>
[ApiController]
[Route("api/v1/imports")]
[Produces("application/json")]
public sealed class ImportsController(ImportService imports, ICurrentUser user) : ControllerBase
{
    /// <summary>Downloads an example CSV with the expected columns.</summary>
    [HttpGet("{kind}/template")]
    [Produces("text/csv")]
    public IActionResult Template(ImportKind kind)
    {
        EnsureAllowed(kind);
        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(ImportService.Templates[kind])).ToArray(), "text/csv", $"{kind.ToString().ToLowerInvariant()}-template.csv");
    }

    /// <summary>
    /// Validates (and, unless <c>dryRun</c>, saves) a CSV file. Nothing is saved if any row has an error.
    /// </summary>
    [HttpPost("{kind}")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<ImportResultDto> Import(ImportKind kind, IFormFile file, [FromQuery] bool dryRun, CancellationToken ct)
    {
        EnsureAllowed(kind);
        if (file.Length > 5 * 1024 * 1024)
        {
            throw new Domain.Common.DomainRuleException("import.too_large", "The file must be smaller than 5 MB.");
        }
        await using var stream = file.OpenReadStream();
        return await imports.ImportAsync(kind, stream, dryRun, ct);
    }

    private void EnsureAllowed(ImportKind kind)
    {
        var permission = kind switch
        {
            ImportKind.Products => Permissions.ProductsManage,
            ImportKind.Customers => Permissions.CustomersManage,
            ImportKind.Locations => Permissions.WarehousesManage,
            _ => throw new NotFoundException(),
        };
        if (!user.HasPermission(permission)) throw new ForbiddenException();
    }
}
