namespace Inventory.Application.Common;

/// <summary>Binary storage for uploads (local disk now; S3-compatible later).</summary>
public interface IFileStorage
{
    /// <summary>Stores the content under a new random key and returns the key.</summary>
    Task<string> SaveAsync(Stream content, CancellationToken cancellationToken);

    Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken);

    Task DeleteAsync(string key, CancellationToken cancellationToken);
}
