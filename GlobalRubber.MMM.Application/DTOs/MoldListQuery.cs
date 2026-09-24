using GlobalRubber.MMM.Application.Common;

namespace GlobalRubber.MMM.Application.DTOs;

/// <summary>
/// Query string of GET /api/v1/molds: paging plus the filters the analysis lists for the mold list (section 18.3:
/// status, productId, lifeState) and the list's code/name search.
/// </summary>
public sealed class MoldListQuery : PaginationRequest
{
    /// <summary>Case-insensitive "contains" match on the mold code or name.</summary>
    public string? Search { get; set; }

    /// <summary>One of the five status values; any other value simply matches nothing.</summary>
    public string? Status { get; set; }

    public int? ProductId { get; set; }

    /// <summary>Normal / Warning / Replace; any other value simply matches nothing.</summary>
    public string? LifeState { get; set; }
}
