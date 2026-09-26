using GlobalRubber.MMM.Application.Common;

namespace GlobalRubber.MMM.Application.DTOs;

/// <summary>
/// Query string of GET /api/v1/machine-maintenance: paging (default 10 per page) plus the analysis' filters (18.4:
/// bucket, machineId) and a search on the PM number or machine code/name.
/// </summary>
public sealed class MachinePmListQuery : PaginationRequest
{
    /// <summary>
    /// The tab: daily / weekly / monthly / yearly (not completed, checklist of that frequency) or completed (MachinePmBucket);
    /// omitted = every PM. Any other value is a 400.
    /// </summary>
    public string? Bucket { get; set; }

    public int? MachineId { get; set; }

    /// <summary>Case-insensitive "contains" match on the PM number, machine code or machine name.</summary>
    public string? Search { get; set; }
}
