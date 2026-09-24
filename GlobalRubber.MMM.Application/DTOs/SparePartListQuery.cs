using GlobalRubber.MMM.Application.Common;

namespace GlobalRubber.MMM.Application.DTOs;

/// <summary>Query string of GET /api/v1/spare-parts: paging plus optional filters.</summary>
public sealed class SparePartListQuery : PaginationRequest
{
    /// <summary>Case-insensitive "contains" match on the spare part code or name.</summary>
    public string? Search { get; set; }

    /// <summary>
    /// Available / Low Stock / Out of Stock; any other value simply matches nothing. (The Low Stock report and the stock
    /// alerts select on it - R-20, BR-29.)
    /// </summary>
    public string? StockStatus { get; set; }

    /// <summary>The linked machine (Machine Details → Spare Parts tab).</summary>
    public int? MachineId { get; set; }

    /// <summary>true = active only, false = inactive only, omitted = both.</summary>
    public bool? IsActive { get; set; }
}
