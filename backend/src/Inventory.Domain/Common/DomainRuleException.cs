namespace Inventory.Domain.Common;

/// <summary>
/// A business rule was violated (e.g. invalid state transition, insufficient stock).
/// Mapped to HTTP 409 (conflict with current state) or 422 (invalid request) by the API.
/// </summary>
public class DomainRuleException : Exception
{
    public DomainRuleException(string code, string message, bool isConflict = false)
        : base(message)
    {
        Code = code;
        IsConflict = isConflict;
    }

    public DomainRuleException() : this("domain.rule", "A business rule was violated.") { }

    public DomainRuleException(string message) : this("domain.rule", message) { }

    public DomainRuleException(string message, Exception innerException) : base(message, innerException)
    {
        Code = "domain.rule";
    }

    /// <summary>Machine-readable code, e.g. <c>warehouse.code_invalid</c>.</summary>
    public string Code { get; } = "domain.rule";

    /// <summary>True when the request conflicts with the current state (409) rather than being invalid (422).</summary>
    public bool IsConflict { get; }
}
