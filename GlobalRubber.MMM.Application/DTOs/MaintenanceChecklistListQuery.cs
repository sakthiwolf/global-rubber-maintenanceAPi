using GlobalRubber.MMM.Application.Common;

namespace GlobalRubber.MMM.Application.DTOs;

/// <summary>Query string of GET /api/v1/maintenance-checklists: paging plus optional filters.</summary>
public sealed class MaintenanceChecklistListQuery : PaginationRequest
{
    /// <summary>Case-insensitive "contains" match on the code or name.</summary>
    public string? Search { get; set; }

    /// <summary>
    /// Machine / Mold; any other value simply matches nothing. (PM scheduling picks a checklist by its Applies To -
    /// analysis 4.10 / Q-16 - so the column is filterable.)
    /// </summary>
    public string? AppliesTo { get; set; }

    /// <summary>true = active only, false = inactive only, omitted = both.</summary>
    public bool? IsActive { get; set; }

    /// <summary>Only the checklists of this machine.</summary>
    public int? MachineId { get; set; }

    /// <summary>Daily / Weekly / Monthly / Yearly; any other value simply matches nothing.</summary>
    public string? Frequency { get; set; }
}
