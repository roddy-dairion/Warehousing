using System.Text.RegularExpressions;

namespace Inventory.Domain.Common;

/// <summary>Small helpers for validating and normalising text fields on entities.</summary>
public static class Text
{
    public static string Required(string? value, int maxLength, string code, string label)
    {
        var v = (value ?? string.Empty).Trim();
        if (v.Length == 0 || v.Length > maxLength)
        {
            throw new DomainRuleException(code, $"{label} is required and must be at most {maxLength} characters.");
        }
        return v;
    }

    public static string? Optional(string? value, int maxLength, string code, string label)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var v = value.Trim();
        if (v.Length > maxLength)
        {
            throw new DomainRuleException(code, $"{label} must be at most {maxLength} characters.");
        }
        return v;
    }

    /// <summary>Upper-cased code that must match <paramref name="pattern"/>.</summary>
    public static string Code(string? value, Regex pattern, string code, string message)
    {
        var v = (value ?? string.Empty).Trim().ToUpperInvariant();
        if (!pattern.IsMatch(v)) throw new DomainRuleException(code, message);
        return v;
    }
}
