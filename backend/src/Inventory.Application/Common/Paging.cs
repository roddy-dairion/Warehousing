using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Application.Common;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

/// <summary>Common list parameters, bound from the query string.</summary>
public class ListQuery
{
    public const int MaxPageSize = 200;

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
    /// <summary>Free text (case-insensitive "contains").</summary>
    public string? Search { get; set; }
    /// <summary>Field name in camelCase, e.g. <c>name</c>. Unknown fields fall back to the default order.</summary>
    public string? SortBy { get; set; }
    /// <summary><c>asc</c> (default) or <c>desc</c>.</summary>
    public string? SortDir { get; set; }
    /// <summary>Filter on active status; omit for both.</summary>
    public bool? IsActive { get; set; }

    public int SafePage => Math.Max(1, Page);
    public int SafePageSize => Math.Clamp(PageSize, 1, MaxPageSize);
    public bool Descending => string.Equals(SortDir, "desc", StringComparison.OrdinalIgnoreCase);

    /// <summary>Lower-cased trimmed search term, or null.</summary>
    public string? Term => string.IsNullOrWhiteSpace(Search) ? null : Search.Trim().ToLowerInvariant();
}

public static class QueryableExtensions
{
    public static async Task<PagedResult<T>> ToPagedAsync<T>(this IQueryable<T> query, ListQuery list, CancellationToken cancellationToken)
    {
        var total = await query.CountAsync(cancellationToken);
        var items = await query.Skip((list.SafePage - 1) * list.SafePageSize).Take(list.SafePageSize).ToListAsync(cancellationToken);
        return new PagedResult<T>(items, list.SafePage, list.SafePageSize, total);
    }

    /// <summary>Sorts by a whitelisted field; always adds <paramref name="tieBreaker"/> for stable paging.</summary>
    public static IOrderedQueryable<T> SortBy<T>(
        this IQueryable<T> query,
        ListQuery list,
        IReadOnlyDictionary<string, Expression<Func<T, object?>>> fields,
        string defaultField,
        Expression<Func<T, object?>> tieBreaker)
    {
        var key = list.SortBy is not null && fields.ContainsKey(list.SortBy) ? list.SortBy : defaultField;
        var selector = fields[key];
        var ordered = list.Descending ? query.OrderByDescending(selector) : query.OrderBy(selector);
        return ordered.ThenBy(tieBreaker);
    }
}
