using System.Security.Cryptography;
using Inventory.Application.Common;
using Inventory.Application.Documents;
using Inventory.Application.Inventory;
using Inventory.Application.Products;
using Inventory.Domain.Common;
using Inventory.Domain.Dispatches;
using Inventory.Domain.Documents;
using Inventory.Domain.Files;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Inventory.Application.Dispatches;

/// <summary>An uploaded file (already received by the API).</summary>
public sealed record UploadedFile(string FileName, long Length, Func<Stream> Open);

public sealed record DeliverCommand(string ReceiverName, DateTimeOffset? DeliveredAt, string? Notes, UploadedFile? Signature, IReadOnlyList<UploadedFile> Photos);

/// <summary>
/// Proof of delivery (Q13): receiver name, a signature drawn on the phone or tablet and photos are all required.
/// Delivery has no stock effect. Afterwards the signed delivery note is generated and emailed to the customer (Q14).
/// </summary>
public sealed class DeliveryService(
    IApplicationDbContext db,
    IStockStore store,
    DispatchService dispatches,
    DocumentService documents,
    IFileStorage files,
    IWarehouseAccess access,
    ICurrentUser user,
    TimeProvider clock,
    IAuditWriter audit,
    IOptions<DispatchOptions> options)
{
    public const string SignatureOwner = "pod_signature";
    public const string PhotoOwner = "pod_photo";
    public const long MaxSignatureBytes = 1024 * 1024;

    public async Task<DispatchDto> DeliverAsync(Guid dispatchId, DeliverCommand command, CancellationToken cancellationToken)
    {
        var o = options.Value;
        if (command.Signature is null) throw new DomainRuleException("pod.signature_required", "The receiver's signature is required.");
        if (command.Photos.Count < o.MinDeliveryPhotos)
        {
            throw new DomainRuleException("pod.photos_required", o.MinDeliveryPhotos == 1 ? "Take at least one photo of the delivered goods." : $"Take at least {o.MinDeliveryPhotos} photos.");
        }
        if (command.Photos.Count > o.MaxDeliveryPhotos) throw new DomainRuleException("pod.too_many_photos", $"At most {o.MaxDeliveryPhotos} photos per delivery.");
        Text.Required(command.ReceiverName, 120, "pod.receiver_required", "Receiver name");

        var signature = await ReadImageAsync(command.Signature, MaxSignatureBytes, "signature", cancellationToken);
        var photos = new List<(UploadedFile File, byte[] Bytes, string ContentType)>();
        foreach (var photo in command.Photos)
        {
            var (bytes, type) = await ReadImageAsync(photo, o.MaxPhotoMegabytes * 1024L * 1024L, "photo", cancellationToken);
            photos.Add((photo, bytes, type));
        }

        var savedKeys = new List<string>();
        Guid deliveryNoteId;
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
            await store.LockRowAsync(DispatchService.Table, dispatchId, cancellationToken);
            var d = await dispatches.LoadAsync(dispatchId, cancellationToken);
            d.EnsureStatus(DispatchStatus.Dispatched);
            var now = clock.GetUtcNow();

            var signatureFile = await StoreAsync(command.Signature.FileName, signature.Bytes, signature.ContentType, SignatureOwner, dispatchId, now, savedKeys, cancellationToken);
            foreach (var p in photos) await StoreAsync(p.File.FileName, p.Bytes, p.ContentType, PhotoOwner, dispatchId, now, savedKeys, cancellationToken);

            var pod = ProofOfDelivery.Create(dispatchId, command.ReceiverName, command.DeliveredAt ?? now, signatureFile.Id, command.Notes, user.IdOrNull, now);
            db.ProofsOfDelivery.Add(pod);
            d.MarkDelivered(user.IdOrNull, now);
            var note = DispatchDocument.Request(dispatchId, DocumentType.DeliveryNote, emailToCustomer: true, user.IdOrNull, now);
            db.DispatchDocuments.Add(note);
            deliveryNoteId = note.Id;
            audit.Add("dispatch.delivered", "dispatch", dispatchId.ToString(), d.WarehouseId, new { d.Number, pod.ReceiverName, photos = photos.Count });
            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
        catch
        {
            // The transaction rolled back: remove the files saved for it.
            foreach (var key in savedKeys) await files.DeleteAsync(key, CancellationToken.None);
            throw;
        }

        await documents.TryGenerateAsync(deliveryNoteId, cancellationToken);
        return await dispatches.GetAsync(dispatchId, cancellationToken);
    }

    /// <summary>Signature or photo of a delivery (access-checked).</summary>
    public async Task<(Stream Content, string ContentType, string FileName)> GetPodFileAsync(Guid dispatchId, Guid fileId, CancellationToken cancellationToken)
    {
        await EnsureAccessAsync(dispatchId, cancellationToken);
        var file = await db.StoredFiles.AsNoTracking()
            .SingleOrDefaultAsync(f => f.Id == fileId && f.OwnerId == dispatchId && (f.OwnerType == SignatureOwner || f.OwnerType == PhotoOwner), cancellationToken)
            ?? throw new NotFoundException("File not found.");
        var stream = await files.OpenReadAsync(file.StorageKey, cancellationToken) ?? throw new NotFoundException("File is missing.");
        return (stream, file.ContentType, file.FileName);
    }

    /// <summary>Emails the latest delivery note again, to the customer or another address.</summary>
    public async Task<DispatchDto> SendDeliveryNoteAsync(Guid dispatchId, string? to, CancellationToken cancellationToken)
    {
        await EnsureAccessAsync(dispatchId, cancellationToken);
        var doc = await db.DispatchDocuments.AsNoTracking()
            .Where(x => x.DispatchId == dispatchId && x.Type == DocumentType.DeliveryNote && x.Status == DocumentStatus.Generated)
            .OrderByDescending(x => x.GeneratedAt).FirstOrDefaultAsync(cancellationToken)
            ?? throw new DomainRuleException("document.not_ready", "There is no generated delivery note yet.", isConflict: true);
        await documents.QueueDeliveryEmailAsync(doc, to, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await dispatches.GetAsync(dispatchId, cancellationToken);
    }

    private async Task EnsureAccessAsync(Guid dispatchId, CancellationToken cancellationToken)
    {
        var warehouseId = await db.Dispatches.Where(d => d.Id == dispatchId).Select(d => (Guid?)d.WarehouseId).SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Dispatch not found.");
        await access.EnsureCanAccessAsync(warehouseId, cancellationToken);
    }

    private static async Task<(byte[] Bytes, string ContentType)> ReadImageAsync(UploadedFile file, long maxBytes, string what, CancellationToken cancellationToken)
    {
        var limitMb = maxBytes / (1024d * 1024d);
        if (file.Length <= 0 || file.Length > maxBytes)
        {
            throw new DomainRuleException($"pod.{what}_size", $"Each {what} must be smaller than {limitMb:0.#} MB.");
        }
        await using var stream = file.Open();
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length > maxBytes) throw new DomainRuleException($"pod.{what}_size", $"Each {what} must be smaller than {limitMb:0.#} MB.");
        var bytes = buffer.ToArray();
        var type = ImageSniffer.Detect(bytes) ?? throw new DomainRuleException($"pod.{what}_type", $"The {what} must be a JPEG, PNG or WebP image.");
        return (bytes, type);
    }

    private async Task<StoredFile> StoreAsync(string fileName, byte[] bytes, string contentType, string owner, Guid dispatchId, DateTimeOffset now,
        List<string> savedKeys, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(bytes);
        var key = await files.SaveAsync(stream, cancellationToken);
        savedKeys.Add(key);
        var name = string.IsNullOrWhiteSpace(Path.GetFileName(fileName)) ? $"{owner}.{contentType[(contentType.IndexOf('/') + 1)..]}" : fileName;
        var stored = StoredFile.Create(name, contentType, bytes.Length, key, Convert.ToHexStringLower(SHA256.HashData(bytes)), owner, dispatchId, now, user.IdOrNull);
        db.StoredFiles.Add(stored);
        return stored;
    }
}
