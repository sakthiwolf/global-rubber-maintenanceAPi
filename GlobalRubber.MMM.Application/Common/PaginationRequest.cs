namespace GlobalRubber.MMM.Application.Common;

/// <summary>
/// Base query parameters accepted by every paginated list endpoint. Module-specific queries
/// (e.g. a future <c>MachineListQuery</c>) inherit from this instead of redeclaring paging.
/// </summary>
public class PaginationRequest
{
    private int _pageNumber = PaginationDefaults.DefaultPageNumber;
    private int _pageSize = PaginationDefaults.DefaultPageSize;

    /// <summary>1-based page number. Values below 1 are clamped to 1.</summary>
    public int PageNumber
    {
        get => _pageNumber;
        set => _pageNumber = value < 1 ? 1 : value;
    }

    /// <summary>
    /// Number of records per page. Values below 1 are clamped to 1; values above
    /// <see cref="PaginationDefaults.MaxPageSize"/> are clamped down to prevent unbounded queries.
    /// </summary>
    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = value < 1 ? 1 : Math.Min(value, PaginationDefaults.MaxPageSize);
    }
}
