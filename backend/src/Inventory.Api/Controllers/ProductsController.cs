using Inventory.Api.Infrastructure;
using Inventory.Application.Common;
using Inventory.Application.Products;
using Inventory.Domain.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace Inventory.Api.Controllers;

[ApiController]
[Route("api/v1/products")]
[Produces("application/json")]
public sealed class ProductsController(ProductService products) : ControllerBase
{
    /// <summary>Products, paged. <c>search</c> matches SKU or name (contains) or a barcode (exact).</summary>
    [HttpGet]
    [HasPermission(Permissions.ProductsRead)]
    public Task<PagedResult<ProductListItemDto>> List([FromQuery] ProductListQuery query, CancellationToken ct) => products.ListAsync(query, ct);

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.ProductsRead)]
    public Task<ProductDto> Get(Guid id, CancellationToken ct) => products.GetAsync(id, ct);

    [HttpPost]
    [HasPermission(Permissions.ProductsManage)]
    [ProducesResponseType<ProductDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<ProductDto>> Create(CreateProductRequest request, CancellationToken ct)
    {
        var created = await products.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.ProductsManage)]
    public Task<ProductDto> Update(Guid id, UpdateProductRequest request, CancellationToken ct) => products.UpdateAsync(id, request, ct);

    [HttpPost("{id:guid}/activate")]
    [HasPermission(Permissions.ProductsManage)]
    public async Task<IActionResult> Activate(Guid id, CancellationToken ct)
    {
        await products.SetActiveAsync(id, true, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/deactivate")]
    [HasPermission(Permissions.ProductsManage)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        await products.SetActiveAsync(id, false, ct);
        return NoContent();
    }

    // ---- Pack units ----

    [HttpPost("{id:guid}/units")]
    [HasPermission(Permissions.ProductsManage)]
    public Task<ProductDto> AddUnit(Guid id, AddUnitRequest request, CancellationToken ct) => products.AddUnitAsync(id, request, ct);

    [HttpPut("{id:guid}/units/{productUnitId:guid}")]
    [HasPermission(Permissions.ProductsManage)]
    public Task<ProductDto> ChangeUnit(Guid id, Guid productUnitId, ChangeUnitRequest request, CancellationToken ct) =>
        products.ChangeUnitAsync(id, productUnitId, request, ct);

    [HttpDelete("{id:guid}/units/{productUnitId:guid}")]
    [HasPermission(Permissions.ProductsManage)]
    public Task<ProductDto> RemoveUnit(Guid id, Guid productUnitId, [FromQuery] uint version, CancellationToken ct) =>
        products.RemoveUnitAsync(id, productUnitId, version, ct);

    // ---- Barcodes ----

    [HttpPost("{id:guid}/barcodes")]
    [HasPermission(Permissions.ProductsManage)]
    public Task<ProductDto> AddBarcode(Guid id, AddBarcodeRequest request, CancellationToken ct) => products.AddBarcodeAsync(id, request, ct);

    [HttpDelete("{id:guid}/barcodes/{barcodeId:guid}")]
    [HasPermission(Permissions.ProductsManage)]
    public Task<ProductDto> RemoveBarcode(Guid id, Guid barcodeId, [FromQuery] uint version, CancellationToken ct) =>
        products.RemoveBarcodeAsync(id, barcodeId, version, ct);

    [HttpPost("{id:guid}/barcodes/{barcodeId:guid}/primary")]
    [HasPermission(Permissions.ProductsManage)]
    public Task<ProductDto> SetPrimaryBarcode(Guid id, Guid barcodeId, [FromQuery] uint version, CancellationToken ct) =>
        products.SetPrimaryBarcodeAsync(id, barcodeId, version, ct);

    // ---- Image ----

    /// <summary>Product image (JPEG/PNG/WebP). Requires a bearer token like every other call.</summary>
    [HttpGet("{id:guid}/image")]
    [HasPermission(Permissions.ProductsRead)]
    [Produces("image/jpeg", "image/png", "image/webp")]
    public async Task<IActionResult> GetImage(Guid id, CancellationToken ct)
    {
        var (content, contentType, sha) = await products.GetImageAsync(id, ct);
        Response.Headers[HeaderNames.CacheControl] = "private, max-age=300";
        Response.Headers[HeaderNames.XContentTypeOptions] = "nosniff";
        return File(content, contentType, entityTag: new EntityTagHeaderValue($"\"{sha}\""), lastModified: null);
    }

    /// <summary>Uploads or replaces the image (multipart field <c>file</c>, max 2 MB).</summary>
    [HttpPut("{id:guid}/image")]
    [HasPermission(Permissions.ProductsManage)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(3 * 1024 * 1024)]
    public async Task<IActionResult> SetImage(Guid id, IFormFile file, CancellationToken ct)
    {
        await using var stream = file.OpenReadStream();
        await products.SetImageAsync(id, file.FileName, stream, file.Length, ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}/image")]
    [HasPermission(Permissions.ProductsManage)]
    public async Task<IActionResult> RemoveImage(Guid id, CancellationToken ct)
    {
        await products.RemoveImageAsync(id, ct);
        return NoContent();
    }
}

/// <summary>Barcode scanning lookup.</summary>
[ApiController]
[Route("api/v1/product-barcodes")]
[Produces("application/json")]
public sealed class ProductBarcodesController(ProductService products) : ControllerBase
{
    /// <summary>Identifies the product (and pack size) behind a scanned barcode. 404 when unknown.</summary>
    [HttpGet("{value}")]
    [HasPermission(Permissions.ProductsRead)]
    [ProducesResponseType<BarcodeLookupDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<BarcodeLookupDto> Lookup(string value, CancellationToken ct) => products.LookupBarcodeAsync(value, ct);
}
