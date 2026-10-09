using System.Linq.Expressions;
using Inventory.Application.Common;
using Inventory.Domain.Common;
using Inventory.Domain.Customers;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Application.Customers;

public sealed class CustomerService(IApplicationDbContext db, IAuditWriter audit)
{
    private static readonly Dictionary<string, Expression<Func<Customer, object?>>> Sorts = new()
    {
        ["reference"] = c => c.Reference,
        ["companyName"] = c => c.CompanyName,
        ["city"] = c => c.BillingCity,
        ["isActive"] = c => c.IsActive,
    };

    public Task<PagedResult<CustomerListItemDto>> ListAsync(ListQuery query, CancellationToken cancellationToken)
    {
        var q = db.Customers.AsNoTracking();
        if (query.Term is { } term)
        {
            q = q.Where(c => c.Reference.ToLower().Contains(term) || c.CompanyName.ToLower().Contains(term)
                || (c.ContactName != null && c.ContactName.ToLower().Contains(term)) || (c.Email != null && c.Email.ToLower().Contains(term)));
        }
        if (query.IsActive is { } active) q = q.Where(c => c.IsActive == active);
        return q.SortBy(query, Sorts, "companyName", c => c.Id)
            .Select(c => new CustomerListItemDto(c.Id, c.Reference, c.CompanyName, c.ContactName, c.Phone, c.Email, c.BillingCity, c.Addresses.Count(a => a.IsActive), c.IsActive))
            .ToPagedAsync(query, cancellationToken);
    }

    public async Task<CustomerDto> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var customer = await db.Customers.AsNoTracking().Include(c => c.Addresses).SingleOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw new NotFoundException("Customer not found.");
        var version = await db.Customers.Where(c => c.Id == id).Select(c => EF.Property<uint>(c, Versioning.Column)).SingleAsync(cancellationToken);
        return ToDto(customer, version);
    }

    public async Task<CustomerDto> CreateAsync(CreateCustomerRequest r, CancellationToken cancellationToken)
    {
        var customer = Customer.Create(r.Reference, r.CompanyName);
        await EnsureReferenceFreeAsync(customer.Reference, cancellationToken);
        customer.SetContact(r.ContactName, r.Phone, r.Email);
        customer.SetBilling(r.BillingLine1, r.BillingLine2, r.BillingCity, r.BillingCountry);
        customer.SetNotes(r.Notes);
        foreach (var a in r.Addresses ?? [])
        {
            customer.AddAddress(a.Label, a.Line1, a.Line2, a.City, a.Country, a.ContactName, a.Phone, a.IsDefault);
        }
        db.Customers.Add(customer);
        audit.Add("customer.created", "customer", customer.Id.ToString(), data: new { customer.Reference, customer.CompanyName });
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(customer.Id, cancellationToken);
    }

    public async Task<CustomerDto> UpdateAsync(Guid id, UpdateCustomerRequest r, CancellationToken cancellationToken)
    {
        var customer = await LoadAsync(id, cancellationToken);
        db.ExpectVersion(customer, r.Version);
        customer.Rename(r.CompanyName);
        customer.SetContact(r.ContactName, r.Phone, r.Email);
        customer.SetBilling(r.BillingLine1, r.BillingLine2, r.BillingCity, r.BillingCountry);
        customer.SetNotes(r.Notes);
        audit.Add("customer.updated", "customer", id.ToString(), data: new { customer.CompanyName });
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task SetActiveAsync(Guid id, bool active, CancellationToken cancellationToken)
    {
        var customer = await LoadAsync(id, cancellationToken);
        if (customer.IsActive == active) return;
        if (active) customer.Activate(); else customer.Deactivate();
        audit.Add(active ? "customer.activated" : "customer.deactivated", "customer", id.ToString());
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task<CustomerDto> AddAddressAsync(Guid id, AddressChangeRequest r, CancellationToken cancellationToken) =>
        ChangeAsync(id, r.Version, "customer.address_added", c =>
        {
            var a = r.Address;
            c.AddAddress(a.Label, a.Line1, a.Line2, a.City, a.Country, a.ContactName, a.Phone, a.IsDefault);
        }, cancellationToken);

    public Task<CustomerDto> UpdateAddressAsync(Guid id, Guid addressId, AddressChangeRequest r, CancellationToken cancellationToken) =>
        ChangeAsync(id, r.Version, "customer.address_updated", c =>
        {
            var a = r.Address;
            c.UpdateAddress(addressId, a.Label, a.Line1, a.Line2, a.City, a.Country, a.ContactName, a.Phone, a.IsDefault);
        }, cancellationToken);

    public Task<CustomerDto> SetAddressActiveAsync(Guid id, Guid addressId, bool active, uint version, CancellationToken cancellationToken) =>
        ChangeAsync(id, version, active ? "customer.address_activated" : "customer.address_deactivated", c =>
        {
            if (active) c.ActivateAddress(addressId); else c.DeactivateAddress(addressId);
        }, cancellationToken);

    internal async Task EnsureReferenceFreeAsync(string reference, CancellationToken cancellationToken)
    {
        if (await db.Customers.AnyAsync(c => c.Reference == reference, cancellationToken))
        {
            throw new DomainRuleException("customer.reference_taken", $"Customer reference {reference} is already used.", isConflict: true);
        }
    }

    private async Task<CustomerDto> ChangeAsync(Guid id, uint version, string action, Action<Customer> change, CancellationToken cancellationToken)
    {
        var customer = await LoadAsync(id, cancellationToken);
        db.ExpectVersion(customer, version);
        change(customer);
        audit.Add(action, "customer", id.ToString());
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    private async Task<Customer> LoadAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Customers.Include(c => c.Addresses).SingleOrDefaultAsync(c => c.Id == id, cancellationToken)
        ?? throw new NotFoundException("Customer not found.");

    private static CustomerDto ToDto(Customer c, uint version) => new(
        c.Id, c.Reference, c.CompanyName, c.ContactName, c.Phone, c.Email,
        c.BillingLine1, c.BillingLine2, c.BillingCity, c.BillingCountry, c.Notes, c.IsActive,
        c.Addresses.OrderByDescending(a => a.IsDefault).ThenBy(a => a.Label)
            .Select(a => new CustomerAddressDto(a.Id, a.Label, a.Line1, a.Line2, a.City, a.Country, a.ContactName, a.Phone, a.IsDefault, a.IsActive))
            .ToList(),
        version);
}
