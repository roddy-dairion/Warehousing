namespace Inventory.Application.Common;

/// <summary>The authenticated caller, resolved from the validated access token (never from request data).</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    /// <summary>The user id. Throws if the caller is anonymous.</summary>
    Guid Id { get; }

    Guid? IdOrNull { get; }

    bool HasPermission(string permission);

    string? IpAddress { get; }

    string? CorrelationId { get; }
}
