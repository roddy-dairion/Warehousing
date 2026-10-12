using System.Security.Cryptography;
using Inventory.Application.Common;
using Inventory.Application.Products;
using Inventory.Domain.Common;
using Inventory.Domain.Documents;
using Inventory.Domain.Files;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Application.Documents;

public sealed record TemplateVersionDto(Guid Id, DocumentType Type, int Version, bool IsCurrent, string? Comment, DateTimeOffset CreatedAt, string? CreatedByName);

public sealed record TemplateDto(DocumentType Type, int Version, string Html, string? Comment, DateTimeOffset CreatedAt, string? CreatedByName,
    IReadOnlyList<TemplateVersionDto> Versions, IReadOnlyList<string> Placeholders);

public sealed record SaveTemplateRequest(string Html, string? Comment);

public sealed record PreviewTemplateRequest(string? Html, Guid? DispatchId);

public sealed record CompanyProfileDto(
    string Name, string? AddressLine1, string? AddressLine2, string? City, string? Country, string? Phone, string? Email, string? Website,
    string? RegistrationNumber, string? VatNumber, string? FooterText, bool HasLogo, string TimeZone,
    string DeliveryEmailSubject, string DeliveryEmailBody, string? DeliveryEmailBcc, uint Version);

public sealed record UpdateCompanyProfileRequest(
    string Name, string? AddressLine1, string? AddressLine2, string? City, string? Country, string? Phone, string? Email, string? Website,
    string? RegistrationNumber, string? VatNumber, string? FooterText, string TimeZone,
    string DeliveryEmailSubject, string DeliveryEmailBody, string? DeliveryEmailBcc, uint Version);

/// <summary>
/// Document templates (Liquid HTML) and company details, managed by administrators. Saving a template creates a new
/// version after checking it parses and renders with sample data; documents always use the current version.
/// </summary>
public sealed class TemplateService(
    IApplicationDbContext db,
    DocumentModelBuilder models,
    ITemplateEngine engine,
    IPdfRenderer pdf,
    IFileStorage files,
    IUserDirectory users,
    ICurrentUser user,
    TimeProvider clock,
    IAuditWriter audit,
    DocumentService documents)
{
    public const long MaxLogoBytes = 1024 * 1024;

    public static string DefaultHtml(DocumentType type)
    {
        using var stream = typeof(TemplateService).Assembly.GetManifestResourceStream($"Templates.{type}.html")
            ?? throw new InvalidOperationException($"Default template for {type} is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public async Task<IReadOnlyList<TemplateDto>> ListAsync(CancellationToken cancellationToken)
    {
        var result = new List<TemplateDto>();
        foreach (var type in Enum.GetValues<DocumentType>()) result.Add(await GetAsync(type, null, cancellationToken));
        return result;
    }

    /// <summary>The current version, or a specific one.</summary>
    public async Task<TemplateDto> GetAsync(DocumentType type, int? version, CancellationToken cancellationToken)
    {
        var all = await db.DocumentTemplates.AsNoTracking().Where(t => t.Type == type).OrderByDescending(t => t.Version).ToListAsync(cancellationToken);
        var t = (version is null ? all.FirstOrDefault(x => x.IsCurrent) : all.FirstOrDefault(x => x.Version == version))
            ?? throw new NotFoundException("Template version not found.");
        var names = await users.NamesAsync(all.Select(x => x.CreatedBy), cancellationToken);
        return new TemplateDto(t.Type, t.Version, t.Html, t.Comment, t.CreatedAt, names.NameOf(t.CreatedBy),
            all.Select(x => new TemplateVersionDto(x.Id, x.Type, x.Version, x.IsCurrent, x.Comment, x.CreatedAt, names.NameOf(x.CreatedBy))).ToList(),
            DocumentModelBuilder.Describe());
    }

    /// <summary>Validates and saves a new version (it becomes current). Invalid templates return 422 with the error.</summary>
    public async Task<TemplateDto> SaveAsync(DocumentType type, SaveTemplateRequest request, CancellationToken cancellationToken)
    {
        await CheckAsync(type, request.Html, cancellationToken);
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var current = await db.DocumentTemplates.AsTracking().Where(t => t.Type == type).ToListAsync(cancellationToken);
        foreach (var t in current.Where(t => t.IsCurrent)) t.Retire();
        await db.SaveChangesAsync(cancellationToken); // retire first: only one current version is allowed per type
        var next = current.Count == 0 ? 1 : current.Max(t => t.Version) + 1;
        db.DocumentTemplates.Add(DocumentTemplate.Create(type, next, request.Html, request.Comment, user.IdOrNull, clock.GetUtcNow()));
        audit.Add("template.saved", "document_template", type.ToString(), data: new { version = next, request.Comment });
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return await GetAsync(type, null, cancellationToken);
    }

    /// <summary>Makes an older version current again (as a new version, so history stays linear).</summary>
    public async Task<TemplateDto> RestoreAsync(DocumentType type, int version, CancellationToken cancellationToken)
    {
        var old = await db.DocumentTemplates.AsNoTracking().SingleOrDefaultAsync(t => t.Type == type && t.Version == version, cancellationToken)
            ?? throw new NotFoundException("Template version not found.");
        return await SaveAsync(type, new SaveTemplateRequest(old.Html, $"Restored version {version}"), cancellationToken);
    }

    /// <summary>
    /// Renders a PDF preview: the given HTML (or the current template) with sample data, or with a real dispatch the user can access.
    /// </summary>
    public async Task<byte[]> PreviewAsync(DocumentType type, PreviewTemplateRequest request, CancellationToken cancellationToken)
    {
        var html = request.Html ?? (await db.DocumentTemplates.AsNoTracking().SingleAsync(t => t.Type == type && t.IsCurrent, cancellationToken)).Html;
        var company = await documents.CompanyAsync(cancellationToken);
        Dictionary<string, object?> model;
        if (request.DispatchId is { } dispatchId)
        {
            var d = await db.Dispatches.AsNoTracking().SingleOrDefaultAsync(x => x.Id == dispatchId, cancellationToken) ?? throw new NotFoundException("Dispatch not found.");
            if (type == DocumentType.DeliveryNote && !await db.ProofsOfDelivery.AnyAsync(p => p.DispatchId == dispatchId, cancellationToken))
            {
                throw new DomainRuleException("template.preview_no_delivery", "That dispatch has no proof of delivery yet; preview with sample data instead.");
            }
            model = await models.BuildAsync(d.Id, type, company, cancellationToken);
        }
        else
        {
            model = await models.SampleAsync(type, company, cancellationToken);
        }
        var rendered = await RenderCheckedAsync(html, model, company.TimeZone, cancellationToken);
        try
        {
            return await pdf.RenderAsync(rendered, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException and not DomainRuleException)
        {
            throw new DomainRuleException("pdf.failed", $"The PDF could not be created: {e.Message}");
        }
    }

    // ---- Company -----------------------------------------------------------------------------

    public async Task<CompanyProfileDto> GetCompanyAsync(CancellationToken cancellationToken)
    {
        var c = await LoadCompanyAsync(cancellationToken);
        var version = db.Of(c);
        return new CompanyProfileDto(c.Name, c.AddressLine1, c.AddressLine2, c.City, c.Country, c.Phone, c.Email, c.Website, c.RegistrationNumber, c.VatNumber,
            c.FooterText, c.LogoFileId is not null, c.TimeZone, c.DeliveryEmailSubject, c.DeliveryEmailBody, c.DeliveryEmailBcc, version);
    }

    public async Task<CompanyProfileDto> UpdateCompanyAsync(UpdateCompanyProfileRequest r, CancellationToken cancellationToken)
    {
        foreach (var t in new[] { r.DeliveryEmailSubject, r.DeliveryEmailBody })
        {
            if (engine.Validate(t ?? string.Empty) is { } error) throw new DomainRuleException("template.invalid", $"Email text: {error}");
        }
        var c = await LoadCompanyAsync(cancellationToken);
        db.ExpectVersion(c, r.Version);
        c.Update(r.Name, r.AddressLine1, r.AddressLine2, r.City, r.Country, r.Phone, r.Email, r.Website, r.RegistrationNumber, r.VatNumber, r.FooterText, r.TimeZone);
        c.SetEmail(r.DeliveryEmailSubject, r.DeliveryEmailBody, r.DeliveryEmailBcc);
        audit.Add("company.updated", "company", c.Id.ToString());
        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
        return await GetCompanyAsync(cancellationToken);
    }

    public async Task SetLogoAsync(string fileName, Stream content, long length, CancellationToken cancellationToken)
    {
        if (length is <= 0 or > MaxLogoBytes) throw new DomainRuleException("image.size", "The logo must be smaller than 1 MB.");
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        var bytes = buffer.ToArray();
        if (bytes.Length > MaxLogoBytes) throw new DomainRuleException("image.size", "The logo must be smaller than 1 MB.");
        var type = ImageSniffer.Detect(bytes) ?? throw new DomainRuleException("image.type", "Only JPEG, PNG or WebP images are accepted.");

        var c = await LoadCompanyAsync(cancellationToken);
        using var stream = new MemoryStream(bytes);
        var key = await files.SaveAsync(stream, cancellationToken);
        var stored = StoredFile.Create(fileName, type, bytes.Length, key, Convert.ToHexStringLower(SHA256.HashData(bytes)), "company_logo", c.Id, clock.GetUtcNow(), user.IdOrNull);
        db.StoredFiles.Add(stored);
        c.SetLogo(stored.Id);
        audit.Add("company.logo_set", "company", c.Id.ToString());
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveLogoAsync(CancellationToken cancellationToken)
    {
        var c = await LoadCompanyAsync(cancellationToken);
        c.SetLogo(null);
        audit.Add("company.logo_removed", "company", c.Id.ToString());
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<(Stream Content, string ContentType)> GetLogoAsync(CancellationToken cancellationToken)
    {
        var c = await LoadCompanyAsync(cancellationToken);
        var file = await db.StoredFiles.AsNoTracking().SingleOrDefaultAsync(f => f.Id == c.LogoFileId, cancellationToken) ?? throw new NotFoundException("No logo.");
        var stream = await files.OpenReadAsync(file.StorageKey, cancellationToken) ?? throw new NotFoundException("Logo file is missing.");
        return (stream, file.ContentType);
    }

    private async Task<CompanyProfile> LoadCompanyAsync(CancellationToken cancellationToken) =>
        await db.CompanyProfiles.AsTracking().OrderBy(c => c.CreatedAt).FirstOrDefaultAsync(cancellationToken)
        ?? throw new NotFoundException("Company profile is not set up.");

    private async Task CheckAsync(DocumentType type, string html, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(html)) throw new DomainRuleException("template.empty", "The template cannot be empty.");
        var company = await documents.CompanyAsync(cancellationToken);
        await RenderCheckedAsync(html, await models.SampleAsync(type, company, cancellationToken), company.TimeZone, cancellationToken);
    }

    private async Task<string> RenderCheckedAsync(string html, Dictionary<string, object?> model, string timeZone, CancellationToken cancellationToken)
    {
        if (engine.Validate(html) is { } error) throw new DomainRuleException("template.invalid", $"The template has an error: {error}");
        try
        {
            return await engine.RenderAsync(html, model, htmlEncode: true, timeZone, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException and not DomainRuleException)
        {
            throw new DomainRuleException("template.render_failed", $"The template could not be rendered: {e.Message}");
        }
    }
}
