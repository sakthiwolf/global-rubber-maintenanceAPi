using GlobalRubber.MMM.Application.Common;

namespace GlobalRubber.MMM.Application.DTOs;

/// <summary>
/// Query string of GET /api/v1/machines: paging plus the filters the analysis lists for the machine list
/// (section 18.3: departmentId, operationalStatus, isActive) and the list's code/name search.
/// </summary>
public sealed class MachineListQuery : PaginationRequest
{
    /// <summary>Case-insensitive "contains" match on the machine code or name.</summary>
    public string? Search { get; set; }

    public int? DepartmentId { get; set; }

    /// <summary>Running / Idle / Breakdown / Maintenance; any other value simply matches nothing.</summary>
    public string? OperationalStatus { get; set; }

    /// <summary>true = active only, false = inactive only, omitted = both.</summary>
    public bool? IsActive { get; set; }
}
