using Inventory.Api.Infrastructure;
using Inventory.Application.Common;
using Inventory.Application.Customers;
using Inventory.Domain.Security;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers;

[ApiController]
[Route("api/v1/customers")]
[Produces("application/json")]
public sealed class CustomersController(CustomerService customers) : ControllerBase
{
    /// <summary>Customers, paged. Readable by customer managers and dispatch staff.</summary>
    [HttpGet]
    [HasPermission(Permissions.CustomersManage, Permissions.DispatchesRead, Permissions.DispatchesCreate)]
    public Task<PagedResult<CustomerListItemDto>> List([FromQuery] ListQuery query, CancellationToken ct) => customers.ListAsync(query, ct);

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.CustomersManage, Permissions.DispatchesRead, Permissions.DispatchesCreate)]
    public Task<CustomerDto> Get(Guid id, CancellationToken ct) => customers.GetAsync(id, ct);

    [HttpPost]
    [HasPermission(Permissions.CustomersManage)]
    [ProducesResponseType<CustomerDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<CustomerDto>> Create(CreateCustomerRequest request, CancellationToken ct)
    {
        var created = await customers.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.CustomersManage)]
    public Task<CustomerDto> Update(Guid id, UpdateCustomerRequest request, CancellationToken ct) => customers.UpdateAsync(id, request, ct);

    [HttpPost("{id:guid}/activate")]
    [HasPermission(Permissions.CustomersManage)]
    public async Task<IActionResult> Activate(Guid id, CancellationToken ct)
    {
        await customers.SetActiveAsync(id, true, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/deactivate")]
    [HasPermission(Permissions.CustomersManage)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        await customers.SetActiveAsync(id, false, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/addresses")]
    [HasPermission(Permissions.CustomersManage)]
    public Task<CustomerDto> AddAddress(Guid id, AddressChangeRequest request, CancellationToken ct) => customers.AddAddressAsync(id, request, ct);

    [HttpPut("{id:guid}/addresses/{addressId:guid}")]
    [HasPermission(Permissions.CustomersManage)]
    public Task<CustomerDto> UpdateAddress(Guid id, Guid addressId, AddressChangeRequest request, CancellationToken ct) =>
        customers.UpdateAddressAsync(id, addressId, request, ct);

    [HttpPost("{id:guid}/addresses/{addressId:guid}/activate")]
    [HasPermission(Permissions.CustomersManage)]
    public Task<CustomerDto> ActivateAddress(Guid id, Guid addressId, [FromQuery] uint version, CancellationToken ct) =>
        customers.SetAddressActiveAsync(id, addressId, true, version, ct);

    [HttpPost("{id:guid}/addresses/{addressId:guid}/deactivate")]
    [HasPermission(Permissions.CustomersManage)]
    public Task<CustomerDto> DeactivateAddress(Guid id, Guid addressId, [FromQuery] uint version, CancellationToken ct) =>
        customers.SetAddressActiveAsync(id, addressId, false, version, ct);
}
