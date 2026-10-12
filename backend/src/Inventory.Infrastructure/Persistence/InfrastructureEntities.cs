namespace Inventory.Infrastructure.Persistence;

/// <summary>Last issued number per document type, warehouse and year (e.g. GR-MAIN-2026-000042).</summary>
public sealed class DocumentSequence
{
    public string Prefix { get; set; } = null!;
    public Guid WarehouseId { get; set; }
    public int Year { get; set; }
    public long LastValue { get; set; }
}

public enum IdempotencyState
{
    InProgress,
    Completed,
}

/// <summary>
/// The stored outcome of a request sent with an <c>Idempotency-Key</c> header. A retry with the same key and body
/// receives this response instead of running the command again.
/// </summary>
public sealed class IdempotencyRecord
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public string Key { get; set; } = null!;
    /// <summary>HTTP method and route path, e.g. <c>POST /api/v1/receipts/{id}/post</c>.</summary>
    public string Endpoint { get; set; } = null!;
    /// <summary>SHA-256 of endpoint + body, so the same key cannot be reused for a different request.</summary>
    public string RequestHash { get; set; } = null!;
    public IdempotencyState State { get; set; }
    public int? ResponseStatus { get; set; }
    public string? ResponseContentType { get; set; }
    public string? ResponseBody { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
