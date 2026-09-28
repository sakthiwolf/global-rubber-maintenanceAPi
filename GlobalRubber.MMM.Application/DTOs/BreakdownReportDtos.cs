using GlobalRubber.MMM.Application.Common;

namespace GlobalRubber.MMM.Application.DTOs;

/// <summary>
/// One group of the Breakdown Analysis: the breakdowns of one machine / type / department / priority / stage / status.
/// Downtime figures use the value the breakdown workflow stored at Resolved - open breakdowns have none.
/// </summary>
public sealed class BreakdownGroupDto
{
    /// <summary>Machine / type / department code, or the priority / stage / status value; null = no breakdown type.</summary>
    public string? Code { get; init; }

    /// <summary>Machine / type / department name or the value; "Unspecified" for breakdowns without a type.</summary>
    public string Name { get; init; } = string.Empty;

    public int BreakdownCount { get; init; }
    public int OpenCount { get; init; }
    public int ResolvedCount { get; init; }

    /// <summary>Resolved breakdowns with a stored downtime.</summary>
    public int DowntimeRecordCount { get; init; }
    public decimal TotalDowntimeHours { get; init; }
    public decimal? AverageDowntimeHours { get; init; }
    public decimal? LongestDowntimeHours { get; init; }
}

/// <summary>Body of GET .../analysis: the totals over the whole filtered set plus one page of groups.</summary>
public sealed class BreakdownAnalysisReportDto
{
    public string GroupBy { get; init; } = string.Empty;
    public MachineDowntimeSummaryDto Summary { get; init; } = new();
    public PagedResult<BreakdownGroupDto> Groups { get; init; } = PagedResult<BreakdownGroupDto>.Empty(1, PaginationDefaults.DefaultPageSize);
}

/// <summary>Filter options for every Breakdown Reports tab, under RPT_BREAKDOWN View. Fixed lists mirror the database CHECKs.</summary>
public sealed class BreakdownReportLookupsDto
{
    public IReadOnlyList<ReportLookupItemDto> Machines { get; init; } = Array.Empty<ReportLookupItemDto>();

    /// <summary>Departments that have at least one machine.</summary>
    public IReadOnlyList<ReportLookupItemDto> Departments { get; init; } = Array.Empty<ReportLookupItemDto>();
    public IReadOnlyList<ReportLookupItemDto> BreakdownTypes { get; init; } = Array.Empty<ReportLookupItemDto>();
    public IReadOnlyList<string> Priorities { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Stages { get; init; } = Array.Empty<string>();

    /// <summary>Open / Resolved.</summary>
    public IReadOnlyList<string> Statuses { get; init; } = Array.Empty<string>();

    /// <summary>Stages a breakdown in the history report can be at (Resolved / Closed).</summary>
    public IReadOnlyList<string> HistoryStages { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> GroupBys { get; init; } = Array.Empty<string>();
}
