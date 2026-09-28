using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Services.Reports;

namespace GlobalRubber.MMM.Application.Interfaces.Repositories;

/// <summary>
/// Read-only data access for what only the Breakdown Reports need: the Analysis groups and the filter options. The
/// breakdown rows and the downtime summary come from IMachineReportRepository (the same queries as Machine Reports).
/// </summary>
public interface IBreakdownReportRepository
{
    /// <summary>One page of groups (page = null: every group) and the number of groups, over the filtered breakdowns.</summary>
    Task<(IReadOnlyList<BreakdownGroupRow> Items, int TotalCount)> GetGroupsAsync(
        BreakdownAnalysisReportQuery query, ReportPage? page, CancellationToken cancellationToken);

    Task<BreakdownReportLookupsDto> GetLookupsAsync(CancellationToken cancellationToken);
}
