using System.Linq.Expressions;
using FluentValidation;
using Inventory.Application.Common;
using Inventory.Domain.Common;
using Inventory.Domain.Purchasing;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Application.Purchasing;

public sealed record SupplierListItemDto(
    Guid Id, string Code, string Name, string? ContactName, string? Phone, string? Email, string? City, int? LeadTimeDays, bool IsActive);

public sealed record SupplierDto(
    Guid Id, string Code, string Name, string? ContactName, string? Phone, string? Email, string? AddressLine1, string? AddressLine2,
    string? City, string? Country, int? LeadTimeDays, string? Notes, bool IsActive, uint Version);

public sealed record CreateSupplierRequest(
    string Code, string Name, string? ContactName, string? Phone, string? Email, string? AddressLine1, string? AddressLine2,
    string? City, string? Country, int? LeadTimeDays, string? Notes);

public sealed record UpdateSupplierRequest(
    string Name, string? ContactName, string? Phone, string? Email, string? AddressLine1, string? AddressLine2,
    string? City, string? Country, int? LeadTimeDays, string? Notes, uint Version);

public sealed class CreateSupplierRequestValidator : AbstractValidator<CreateSupplierRequest>
{
    public CreateSupplierRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().WithMessage("Enter a code.").Matches("^[A-Za-z0-9][A-Za-z0-9-]{0,31}$").WithMessage("1–32 letters, digits or hyphens.");
        RuleFor(x => x.Name).NotEmpty().WithMessage("Enter the supplier name.").MaximumLength(200);
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email)).WithMessage("Enter a valid email address.");
        RuleFor(x => x.LeadTimeDays).InclusiveBetween(0, 365).When(x => x.LeadTimeDays is not null);
        RuleFor(x => x.Notes).MaximumLength(2000);
    }
}

public sealed class UpdateSupplierRequestValidator : AbstractValidator<UpdateSupplierRequest>
{
    public UpdateSupplierRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Enter the supplier name.").MaximumLength(200);
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email)).WithMessage("Enter a valid email address.");
        RuleFor(x => x.LeadTimeDays).InclusiveBetween(0, 365).When(x => x.LeadTimeDays is not null);
        RuleFor(x => x.Notes).MaximumLength(2000);
    }
}

/// <summary>Suppliers that goods are ordered from. Shared by all warehouses (like products and customers).</summary>
public sealed class SupplierService(IApplicationDbContext db, IAuditWriter audit)
{
    private static readonly Dictionary<string, Expression<Func<Supplier, object?>>> Sorts = new()
    {
        ["code"] = s => s.Code,
        ["name"] = s => s.Name,
        ["city"] = s => s.City,
        ["leadTimeDays"] = s => s.LeadTimeDays,
        ["isActive"] = s => s.IsActive,
    };

    public Task<PagedResult<SupplierListItemDto>> ListAsync(ListQuery query, CancellationToken cancellationToken)
    {
        var q = db.Suppliers.AsNoTracking();
        if (query.Term is { } term)
        {
            q = q.Where(s => s.Code.ToLower().Contains(term) || s.Name.ToLower().Contains(term)
                || (s.ContactName != null && s.ContactName.ToLower().Contains(term)) || (s.Email != null && s.Email.ToLower().Contains(term)));
        }
        if (query.IsActive is { } active) q = q.Where(s => s.IsActive == active);
        return q.SortBy(query, Sorts, "name", s => s.Id)
            .Select(s => new SupplierListItemDto(s.Id, s.Code, s.Name, s.ContactName, s.Phone, s.Email, s.City, s.LeadTimeDays, s.IsActive))
            .ToPagedAsync(query, cancellationToken);
    }

    public async Task<SupplierDto> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var s = await db.Suppliers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken) ?? throw new NotFoundException("Supplier not found.");
        var version = await db.Suppliers.Where(x => x.Id == id).Select(x => EF.Property<uint>(x, Versioning.Column)).SingleAsync(cancellationToken);
        return new SupplierDto(s.Id, s.Code, s.Name, s.ContactName, s.Phone, s.Email, s.AddressLine1, s.AddressLine2, s.City, s.Country,
            s.LeadTimeDays, s.Notes, s.IsActive, version);
    }

    public async Task<SupplierDto> CreateAsync(CreateSupplierRequest r, CancellationToken cancellationToken)
    {
        var supplier = Supplier.Create(r.Code, r.Name);
        if (await db.Suppliers.AnyAsync(s => s.Code == supplier.Code, cancellationToken))
        {
            throw new DomainRuleException("supplier.code_taken", $"Supplier code {supplier.Code} is already used.", isConflict: true);
        }
        supplier.SetContact(r.ContactName, r.Phone, r.Email);
        supplier.SetAddress(r.AddressLine1, r.AddressLine2, r.City, r.Country);
        supplier.SetTerms(r.LeadTimeDays, r.Notes);
        db.Suppliers.Add(supplier);
        audit.Add("supplier.created", "supplier", supplier.Id.ToString(), data: new { supplier.Code, supplier.Name });
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(supplier.Id, cancellationToken);
    }

    public async Task<SupplierDto> UpdateAsync(Guid id, UpdateSupplierRequest r, CancellationToken cancellationToken)
    {
        var supplier = await LoadAsync(id, cancellationToken);
        db.ExpectVersion(supplier, r.Version);
        supplier.Rename(r.Name);
        supplier.SetContact(r.ContactName, r.Phone, r.Email);
        supplier.SetAddress(r.AddressLine1, r.AddressLine2, r.City, r.Country);
        supplier.SetTerms(r.LeadTimeDays, r.Notes);
        audit.Add("supplier.updated", "supplier", id.ToString(), data: new { supplier.Name });
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    /// <summary>An inactive supplier keeps its past orders but cannot be chosen for new ones.</summary>
    public async Task SetActiveAsync(Guid id, bool active, CancellationToken cancellationToken)
    {
        var supplier = await LoadAsync(id, cancellationToken);
        if (supplier.IsActive == active) return;
        if (active) supplier.Activate(); else supplier.Deactivate();
        audit.Add(active ? "supplier.activated" : "supplier.deactivated", "supplier", id.ToString());
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<Supplier> LoadAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Suppliers.SingleOrDefaultAsync(s => s.Id == id, cancellationToken) ?? throw new NotFoundException("Supplier not found.");
}
