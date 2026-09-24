using GlobalRubber.MMM.Application.Common;

namespace GlobalRubber.MMM.Application.DTOs;

/// <summary>Query string of GET /api/v1/maintenance-types: paging plus optional filters.</summary>
public sealed class MaintenanceTypeListQuery : PaginationRequest
{
    /// <summary>Case-insensitive "contains" match on the code or name.</summary>
    public string? Search { get; set; }

    /// <summary>
    /// Machine / Mold / Both; any other value simply matches nothing. (The Machine PM screen needs the types that apply to
    /// Machine or Both - BR-35 - so the column is filterable.)
    /// </summary>
    public string? AppliesTo { get; set; }

    /// <summary>true = active only, false = inactive only, omitted = both.</summary>
    public bool? IsActive { get; set; }
}
