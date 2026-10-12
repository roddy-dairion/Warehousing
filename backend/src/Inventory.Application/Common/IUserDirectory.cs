namespace Inventory.Application.Common;

/// <summary>Display names of users, for "posted by", "approved by" etc.</summary>
public interface IUserDirectory
{
    Task<IReadOnlyDictionary<Guid, string>> NamesAsync(IEnumerable<Guid?> ids, CancellationToken cancellationToken);
}

public static class UserDirectoryExtensions
{
    public static string? NameOf(this IReadOnlyDictionary<Guid, string> names, Guid? id) =>
        id is { } v && names.TryGetValue(v, out var n) ? n : null;
}
