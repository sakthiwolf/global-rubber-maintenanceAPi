using GlobalRubber.MMM.Application.Common;

namespace GlobalRubber.MMM.Application.DTOs;

/// <summary>Query string of GET /api/v1/breakdown-types: paging plus optional filters.</summary>
public sealed class BreakdownTypeListQuery : PaginationRequest
{
    /// <summary>Case-insensitive "contains" match on the code or name.</summary>
    public string? Search { get; set; }

    /// <summary>true = active only, false = inactive only, omitted = both.</summary>
    public bool? IsActive { get; set; }
}
