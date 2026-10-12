using System.Globalization;
using System.Text.Encodings.Web;
using Fluid;
using Inventory.Application.Documents;

namespace Inventory.Infrastructure.Documents;

/// <summary>
/// Liquid templates with Fluid. Output values are HTML-encoded for documents (so customer data cannot inject markup);
/// templates can only read the model they are given, and loops are capped to stop runaway templates.
/// </summary>
public sealed class FluidTemplateEngine : ITemplateEngine
{
    private static readonly FluidParser Parser = new();

    public string? Validate(string template) => Parser.TryParse(template ?? string.Empty, out _, out var error) ? null : error;

    public async Task<string> RenderAsync(string template, IReadOnlyDictionary<string, object?> model, bool htmlEncode, string timeZone, CancellationToken cancellationToken)
    {
        if (!Parser.TryParse(template, out var parsed, out var error))
        {
            throw new InvalidOperationException(error);
        }
        var options = new TemplateOptions
        {
            CultureInfo = CultureInfo.InvariantCulture,
            TimeZone = Inventory.Domain.Common.TimeZones.FindOrUtc(timeZone),
            MaxSteps = 200_000,
            MaxRecursion = 50,
        };
        var context = new TemplateContext(options);
        foreach (var (key, value) in model) context.SetValue(key, value);
        cancellationToken.ThrowIfCancellationRequested();
        return await parsed.RenderAsync(context, htmlEncode ? HtmlEncoder.Default : NullEncoder.Default);
    }
}
