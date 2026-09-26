using GlobalRubber.MMM.Application.Common;

namespace GlobalRubber.MMM.Application.DTOs;

/// <summary>
/// Query string of GET /api/v1/production-entries: paging (default 10 per page) plus the filters the analysis lists
/// (18.4: from, to, machineId, moldId) and a search on the entry number. Results are newest first (6.1).
/// </summary>
public sealed class ProductionEntryListQuery : PaginationRequest
{
    /// <summary>Case-insensitive "contains" match on the entry number.</summary>
    public string? Search { get; set; }

    /// <summary>Entries on or after this date.</summary>
    public DateOnly? FromDate { get; set; }

    /// <summary>Entries on or before this date.</summary>
    public DateOnly? ToDate { get; set; }

    public int? MachineId { get; set; }
    public int? MoldId { get; set; }
}
