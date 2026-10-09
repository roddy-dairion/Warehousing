using Inventory.Application.Common;
using Microsoft.Extensions.Options;

namespace Inventory.Infrastructure.Services;

public sealed class StorageOptions
{
    public const string Section = "Storage";

    /// <summary>Folder for uploaded files. Relative paths are resolved against the API's content root.</summary>
    public string RootPath { get; set; } = "App_Data/files";
}

/// <summary>Stores uploads on local disk under random names, sharded by the first two characters.</summary>
internal sealed class LocalFileStorage(IOptions<StorageOptions> options, Microsoft.Extensions.Hosting.IHostEnvironment env) : IFileStorage
{
    private readonly string _root = Path.GetFullPath(Path.Combine(env.ContentRootPath, options.Value.RootPath));

    public async Task<string> SaveAsync(Stream content, CancellationToken cancellationToken)
    {
        var key = Guid.NewGuid().ToString("N");
        var path = PathFor(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await content.CopyToAsync(file, cancellationToken);
        return key;
    }

    public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken)
    {
        var path = PathFor(key);
        Stream? stream = File.Exists(path) ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true) : null;
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        var path = PathFor(key);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    /// <summary>Keys are generated hex GUIDs; anything else is rejected to rule out path traversal.</summary>
    private string PathFor(string key)
    {
        if (key.Length != 32 || !key.All(Uri.IsHexDigit))
        {
            throw new ArgumentException("Invalid storage key.", nameof(key));
        }
        return Path.Combine(_root, key[..2], key);
    }
}
