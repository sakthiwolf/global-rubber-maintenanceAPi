namespace GlobalRubber.MMM.Application.DTOs;

/// <summary>
/// Query string of GET /api/v1/reports/breakdowns/analysis (and .../export): every breakdown filter of
/// MachineBreakdownReportQuery, plus the dimension to group by. SortBy here sorts the GROUPS: count (default) / downtime /
/// name (BreakdownReportGroupSort).
/// </summary>
public sealed class BreakdownAnalysisReportQuery : MachineBreakdownReportQuery
{
    /// <summary>machine (default) / type / department / priority / stage / status (BreakdownReportGroupBy).</summary>
    public string? GroupBy { get; set; }
}
