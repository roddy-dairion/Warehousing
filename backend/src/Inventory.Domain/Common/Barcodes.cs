using System.Text.RegularExpressions;

namespace Inventory.Domain.Common;

/// <summary>Barcode value rules shared by products and bin labels.</summary>
public static partial class Barcodes
{
    public const int MaxLength = 64;

    /// <summary>Trims, rejects whitespace/control characters. Case is preserved (Code 128 is case-sensitive).</summary>
    public static string Normalize(string? value)
    {
        var v = (value ?? string.Empty).Trim();
        if (!Pattern().IsMatch(v))
        {
            throw new DomainRuleException("barcode.invalid", $"Barcode must be 1–{MaxLength} printable characters without spaces.");
        }
        return v;
    }

    public static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : Normalize(value);

    [GeneratedRegex(@"^[\x21-\x7E]{1,64}$")]
    private static partial Regex Pattern();
}
