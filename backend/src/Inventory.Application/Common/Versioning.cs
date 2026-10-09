using Inventory.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Application.Common;

/// <summary>
/// Optimistic concurrency using PostgreSQL's <c>xmin</c> system column. Clients send back the
/// <c>version</c> they loaded; a stale version fails the update with 409.
/// </summary>
public static class Versioning
{
    public const string Column = "xmin";

    /// <summary>Use in projections: <c>Version = EF.Property&lt;uint&gt;(x, Versioning.Column)</c>.</summary>
    public static uint Of(this IApplicationDbContext db, object entity) => (uint)db.Entry(entity).Property(Column).CurrentValue!;

    /// <summary>Expects the row to still be at <paramref name="version"/> when saved, and marks it modified.</summary>
    public static void ExpectVersion<T>(this IApplicationDbContext db, T entity, uint version) where T : class
    {
        var entry = db.Entry(entity);
        entry.Property(Column).OriginalValue = version;
        if (entity is IAuditable)
        {
            // Ensures the parent row is written (and its version bumped) even when only child rows changed.
            entry.Property(nameof(IAuditable.UpdatedAt)).IsModified = true;
        }
    }
}
