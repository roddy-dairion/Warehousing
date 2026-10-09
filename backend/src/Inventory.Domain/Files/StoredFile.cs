using Inventory.Domain.Common;

namespace Inventory.Domain.Files;

/// <summary>Metadata of an uploaded file (product image, proof of delivery...). The bytes live in file storage.</summary>
public sealed class StoredFile : Entity
{
    private StoredFile() { }

    public string FileName { get; private set; } = null!;
    public string ContentType { get; private set; } = null!;
    public long SizeBytes { get; private set; }
    /// <summary>Random storage name; never derived from the uploaded file name.</summary>
    public string StorageKey { get; private set; } = null!;
    public string Sha256 { get; private set; } = null!;
    public string OwnerType { get; private set; } = null!;
    public Guid OwnerId { get; private set; }
    public DateTimeOffset UploadedAt { get; private set; }
    public Guid? UploadedBy { get; private set; }

    public static StoredFile Create(string fileName, string contentType, long sizeBytes, string storageKey, string sha256, string ownerType, Guid ownerId, DateTimeOffset uploadedAt, Guid? uploadedBy) =>
        new()
        {
            FileName = Text.Required(Path.GetFileName(fileName), 255, "file.name_invalid", "File name"),
            ContentType = contentType,
            SizeBytes = sizeBytes,
            StorageKey = storageKey,
            Sha256 = sha256,
            OwnerType = ownerType,
            OwnerId = ownerId,
            UploadedAt = uploadedAt,
            UploadedBy = uploadedBy,
        };
}
