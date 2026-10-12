using System.Net;
using Inventory.Application.Dispatches;
using Inventory.Application.Documents;
using Inventory.Domain.Dispatches;
using Inventory.Domain.Documents;
using Inventory.Infrastructure.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Inventory.IntegrationTests.Http;

namespace Inventory.IntegrationTests;

public sealed class DeliveryAndDocumentTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    /// <summary>PDF tests need Chrome, Chromium or Edge on the machine; they are skipped (not failed) without one.</summary>
    private static void RequirePdf(DispatchDocumentDto doc)
    {
        if (doc.Status != DocumentStatus.Generated && doc.LastError?.Contains("No Chrome", StringComparison.Ordinal) == true)
        {
            Assert.Skip("No Chrome/Chromium/Edge browser on this machine: " + doc.LastError);
        }
        doc.Status.ShouldBe(DocumentStatus.Generated, doc.LastError);
    }

    private static async Task<byte[]> DownloadAsync(HttpClient client, Guid dispatchId, Guid documentId)
    {
        var response = await client.GetAsync($"/api/v1/dispatches/{dispatchId}/documents/{documentId}", Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
        return await response.Content.ReadAsByteArrayAsync(Ct);
    }

    [Fact]
    public async Task Proof_of_delivery_needs_signature_name_and_photos()
    {
        var kit = await DispatchKit.CreateAsync(api);
        var customer = await kit.CustomerAsync();
        var (p, _, _) = await kit.StockedProductAsync(10);
        var d = await kit.ShippedAsync(customer.Id, DispatchKit.Line(p.Id, 3));

        (await (await kit.DeliverAsync(d.Id, DispatchKit.DeliveryForm(signature: false))).ProblemAsync(HttpStatusCode.UnprocessableEntity)).Code().ShouldBe("pod.signature_required");
        (await (await kit.DeliverAsync(d.Id, DispatchKit.DeliveryForm(photos: 0))).ProblemAsync(HttpStatusCode.UnprocessableEntity)).Code().ShouldBe("pod.photos_required");
        (await (await kit.DeliverAsync(d.Id, DispatchKit.DeliveryForm(receiver: "  "))).ProblemAsync(HttpStatusCode.UnprocessableEntity)).Code().ShouldBe("pod.receiver_required");
        (await (await kit.DeliverAsync(d.Id, DispatchKit.DeliveryForm(photoBytes: "not an image"u8.ToArray()))).ProblemAsync(HttpStatusCode.UnprocessableEntity))
            .Code().ShouldBe("pod.photo_type");
        (await kit.Operator.GetJsonAsync<DispatchDto>($"/api/v1/dispatches/{d.Id}"))!.Status.ShouldBe(DispatchStatus.Dispatched);

        var approvedOnly = await kit.ApprovedAsync(customer.Id, DispatchKit.Line(p.Id, 1));
        (await (await kit.DeliverAsync(approvedOnly.Id, DispatchKit.DeliveryForm())).ProblemAsync(HttpStatusCode.Conflict)).Code().ShouldBe("dispatch.invalid_status");
    }

    [Fact]
    public async Task Delivery_records_proof_generates_the_signed_note_and_emails_the_customer()
    {
        var kit = await DispatchKit.CreateAsync(api);
        var customer = await kit.CustomerAsync(email: "buyer@customer.test");
        var (p, _, _) = await kit.StockedProductAsync(10, lots: true, lot: "L-77");
        var d = await kit.ShippedAsync(customer.Id, DispatchKit.Line(p.Id, 4));
        var slip = d.Documents.Single(x => x.Type == DocumentType.PackingSlip);
        RequirePdf(slip);
        (await DownloadAsync(kit.Operator, d.Id, slip.Id))[..4].ShouldBe("%PDF"u8.ToArray());
        var onHandBefore = await kit.Stock.OnHandAsync(p.Id);

        var delivered = await (await kit.DeliverAsync(d.Id, DispatchKit.DeliveryForm(receiver: "John Receiver", photos: 2))).ReadAsync<DispatchDto>();
        delivered.Status.ShouldBe(DispatchStatus.Delivered);
        var pod = delivered.ProofOfDelivery!;
        pod.ReceiverName.ShouldBe("John Receiver");
        pod.PhotoFileIds.Count.ShouldBe(2);
        (await kit.Stock.OnHandAsync(p.Id)).ShouldBe(onHandBefore); // delivery never touches stock

        var signature = await kit.Manager.GetAsync($"/api/v1/dispatches/{d.Id}/pod/files/{pod.SignatureFileId}", Ct);
        signature.StatusCode.ShouldBe(HttpStatusCode.OK);
        signature.Content.Headers.ContentType!.MediaType.ShouldBe("image/png");

        var note = delivered.Documents.Single(x => x.Type == DocumentType.DeliveryNote);
        RequirePdf(note);
        note.FileName.ShouldBe($"delivery-note-{d.Number}.pdf");
        (await DownloadAsync(kit.Manager, d.Id, note.Id))[..4].ShouldBe("%PDF"u8.ToArray());

        var email = delivered.Emails.ShouldHaveSingleItem();
        (email.To, email.Status).ShouldBe(("buyer@customer.test", EmailStatus.Pending));
        email.Subject.ShouldBe($"Delivery note {d.Number}");

        // The worker sends it (pickup folder in tests).
        var sent = await api.WithDbAsync((_, sp) => sp.GetRequiredService<OutboxProcessor>().ProcessEmailsAsync(Ct));
        sent.ShouldBeGreaterThanOrEqualTo(1);
        var eml = Directory.GetFiles(api.MailDirectory, "*.eml").Select(File.ReadAllText).Single(t => t.Contains(d.Number, StringComparison.Ordinal));
        eml.ShouldContain("buyer@customer.test");
        eml.ShouldContain("dispatch@hellocherie.ch");
        eml.ShouldContain($"delivery-note-{d.Number}.pdf");
        eml.ShouldContain("John Receiver");
        (await kit.Manager.GetJsonAsync<DispatchDto>($"/api/v1/dispatches/{d.Id}"))!.Emails.Single().Status.ShouldBe(EmailStatus.Sent);

        // Resend to another address.
        var resent = await (await kit.Operator.PostJsonAsync($"/api/v1/dispatches/{d.Id}/delivery-note/send", new { to = "office@customer.test" })).ReadAsync<DispatchDto>();
        resent.Emails.Count.ShouldBe(2);
        (await kit.DeliverAsync(d.Id, DispatchKit.DeliveryForm())).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Without_a_customer_email_nothing_is_queued_but_the_note_is_available()
    {
        var kit = await DispatchKit.CreateAsync(api);
        var customer = await kit.CustomerAsync(email: null);
        var (p, _, _) = await kit.StockedProductAsync(5);
        var d = await kit.ShippedAsync(customer.Id, DispatchKit.Line(p.Id, 1));
        var delivered = await (await kit.DeliverAsync(d.Id, DispatchKit.DeliveryForm())).ReadAsync<DispatchDto>();
        RequirePdf(delivered.Documents.Single(x => x.Type == DocumentType.DeliveryNote));
        delivered.Emails.ShouldBeEmpty();
        (await api.WithDbAsync((db, _) => db.AuditLogs.AnyAsync(a => a.Action == "delivery_note.no_recipient" && a.EntityId == d.Id.ToString()))).ShouldBeTrue();
    }

    [Fact]
    public async Task Templates_are_versioned_validated_and_previewable()
    {
        var kit = await DispatchKit.CreateAsync(api);
        var templates = (await kit.Admin.GetJsonAsync<List<TemplateDto>>("/api/v1/document-templates"))!;
        templates.Select(t => t.Type).ShouldBe([DocumentType.PackingSlip, DocumentType.DeliveryNote], ignoreOrder: true);
        var current = templates.Single(t => t.Type == DocumentType.PackingSlip);
        current.Placeholders.ShouldNotBeEmpty();
        (await kit.Manager.GetAsync("/api/v1/document-templates", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var broken = await kit.Admin.PutJsonAsync("/api/v1/document-templates/packingSlip", new { html = "<p>{% for x in lines %}</p>" });
        (await broken.ProblemAsync(HttpStatusCode.UnprocessableEntity)).Code().ShouldBe("template.invalid");

        var html = "<html><body><h1>Slip {{ dispatch.number }}</h1><p>{{ customer.name }}</p>{% for l in lines %}<div>{{ l.sku }} {{ l.shipped }}</div>{% endfor %}</body></html>";
        var saved = await (await kit.Admin.PutJsonAsync("/api/v1/document-templates/packingSlip", new { html, comment = "Simpler" })).ReadAsync<TemplateDto>();
        saved.Version.ShouldBe(current.Version + 1);
        saved.Versions.Count(v => v.IsCurrent).ShouldBe(1);

        var preview = await kit.Admin.PostJsonAsync("/api/v1/document-templates/packingSlip/preview", new { html = (string?)null });
        if (preview.StatusCode == HttpStatusCode.UnprocessableEntity || preview.StatusCode == HttpStatusCode.InternalServerError)
        {
            var body = await preview.Content.ReadAsStringAsync(Ct);
            if (body.Contains("No Chrome", StringComparison.Ordinal)) Assert.Skip("No browser for PDF rendering.");
        }
        preview.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await preview.Content.ReadAsByteArrayAsync(Ct))[..4].ShouldBe("%PDF"u8.ToArray());

        // Documents use the current version.
        var customer = await kit.CustomerAsync();
        var (p, _, _) = await kit.StockedProductAsync(3);
        var d = await kit.ShippedAsync(customer.Id, DispatchKit.Line(p.Id, 1));
        d.Documents.Single().TemplateVersion.ShouldBe(saved.Version);

        var restored = await (await kit.Admin.PostAsync($"/api/v1/document-templates/packingSlip/versions/{current.Version}/restore")).ReadAsync<TemplateDto>();
        restored.Version.ShouldBe(saved.Version + 1);
        restored.Html.ShouldBe(current.Html);
        var regenerated = await (await kit.Manager.PostAsync($"/api/v1/dispatches/{d.Id}/documents/packingSlip")).ReadAsync<DispatchDto>();
        regenerated.Documents.Count.ShouldBe(2);
        regenerated.Documents[^1].TemplateVersion.ShouldBe(restored.Version);
    }

    [Fact]
    public async Task Template_output_is_html_encoded()
    {
        var engine = new FluidTemplateEngine();
        var model = new Dictionary<string, object?> { ["customer"] = new Dictionary<string, object?> { ["name"] = "<script>alert(1)</script> & Co" } };
        var html = await engine.RenderAsync("<p>{{ customer.name }}</p>", model, htmlEncode: true, "UTC", Ct);
        html.ShouldNotContain("<script>");
        html.ShouldContain("&lt;script&gt;");
        (await engine.RenderAsync("{{ customer.name }}", model, htmlEncode: false, "UTC", Ct)).ShouldBe("<script>alert(1)</script> & Co");
        engine.Validate("{% if %}").ShouldNotBeNull();
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Company_profile_can_be_edited_by_administrators()
    {
        var kit = await DispatchKit.CreateAsync(api);
        var company = (await kit.Admin.GetJsonAsync<CompanyProfileDto>("/api/v1/settings/company"))!;
        company.Email.ShouldBe("dispatch@hellocherie.ch");
        company.TimeZone.ShouldBe("Indian/Mauritius");

        var update = new
        {
            name = "HelloCherie Ltd", addressLine1 = "1 Rue du Lac", city = "Geneva", country = "Switzerland", email = "dispatch@hellocherie.ch",
            timeZone = "Mars/Olympus", deliveryEmailSubject = company.DeliveryEmailSubject, deliveryEmailBody = company.DeliveryEmailBody, version = company.Version,
        };
        (await (await kit.Admin.PutJsonAsync("/api/v1/settings/company", update)).ProblemAsync(HttpStatusCode.UnprocessableEntity)).Code().ShouldBe("company.time_zone_invalid");
        var saved = await (await kit.Admin.PutJsonAsync("/api/v1/settings/company", update with { timeZone = "Europe/Zurich" })).ReadAsync<CompanyProfileDto>();
        (saved.Name, saved.City, saved.TimeZone).ShouldBe(("HelloCherie Ltd", "Geneva", "Europe/Zurich"));

        using var logo = new MultipartFormDataContent();
        var bytes = new ByteArrayContent(DispatchKit.Png);
        bytes.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        logo.Add(bytes, "file", "logo.png");
        (await kit.Admin.PostAsync("/api/v1/settings/company/logo", logo, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await kit.Admin.GetJsonAsync<CompanyProfileDto>("/api/v1/settings/company"))!.HasLogo.ShouldBeTrue();
        (await kit.Operator.GetAsync("/api/v1/settings/company", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
