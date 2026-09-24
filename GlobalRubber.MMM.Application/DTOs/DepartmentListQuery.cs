using GlobalRubber.MMM.Application.Common;

namespace GlobalRubber.MMM.Application.DTOs;

/// <summary>Query string of GET /api/v1/departments: paging plus optional filters.</summary>
public sealed class DepartmentListQuery : PaginationRequest
{
    /// <summary>Case-insensitive "contains" match on the department code or name.</summary>
    public string? Search { get; set; }

    /// <summary>true = active only, false = inactive only, omitted = both.</summary>
    public bool? IsActive { get; set; }
}
