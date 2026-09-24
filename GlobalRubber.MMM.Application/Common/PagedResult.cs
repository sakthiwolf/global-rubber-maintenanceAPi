using System.Text.Json.Serialization;

namespace GlobalRubber.MMM.Application.Common;

/// <summary>
/// A page of <typeparamref name="T"/> plus its paging metadata. This is the shape every
/// list endpoint (Machines, Molds, Breakdowns, ...) will return once those modules exist.
/// </summary>
/// <typeparam name="T">The row/list-item type being paged.</typeparam>
public sealed class PagedResult<T>
{
    [JsonPropertyOrder(0)]
    public IReadOnlyList<T> Items { get; init; } = Array.Empty<T>();

    [JsonPropertyOrder(1)]
    public int PageNumber { get; init; }

    [JsonPropertyOrder(2)]
    public int PageSize { get; init; }

    [JsonPropertyOrder(3)]
    public int TotalCount { get; init; }

    [JsonPropertyOrder(4)]
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    [JsonPropertyOrder(5)]
    public bool HasPreviousPage => PageNumber > 1;

    [JsonPropertyOrder(6)]
    public bool HasNextPage => PageNumber < TotalPages;

    public static PagedResult<T> Create(IReadOnlyList<T> items, int pageNumber, int pageSize, int totalCount) =>
        new()
        {
            Items = items,
            PageNumber = pageNumber,
            PageSize = pageSize,
            TotalCount = totalCount,
        };

    public static PagedResult<T> Empty(int pageNumber, int pageSize) =>
        Create(Array.Empty<T>(), pageNumber, pageSize, totalCount: 0);
}
