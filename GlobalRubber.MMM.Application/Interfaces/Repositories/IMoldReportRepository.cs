using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Services.Reports;

namespace GlobalRubber.MMM.Application.Interfaces.Repositories;

/// <summary>
/// Read-only data access for the Mold Reports: filters, sorts, pages and aggregates in the database (untracked) with
/// MoldReportQueryBuilder. <c>page</c> = null returns the whole filtered set (export). Nothing here writes.
/// </summary>
public interface IMoldReportRepository
{
    Task<(IReadOnlyList<MoldListRow> Items, int TotalCount)> GetMoldListAsync(
        MoldListReportQuery query, ReportPage? page, CancellationToken cancellationToken);

    Task<(IReadOnlyList<MoldUsageRow> Items, int TotalCount)> GetUsageAsync(
        MoldUsageReportQuery query, ReportPage? page, CancellationToken cancellationToken);

    /// <param name="replacementScope">true = the Replacement report (molds needing replacement or Retired).</param>
    Task<(IReadOnlyList<MoldBaseRow> Items, int TotalCount)> GetLifeAsync(
        MoldLifeReportQuery query, bool replacementScope, ReportPage? page, CancellationToken cancellationToken);

    /// <summary>Counts over the whole filtered set.</summary>
    Task<MoldLifeSummaryDto> GetLifeSummaryAsync(
        MoldLifeReportQuery query, bool replacementScope, CancellationToken cancellationToken);

    /// <param name="today">Plant (IST) date - Due vs Overdue.</param>
    Task<(IReadOnlyList<MoldMaintenanceReportItemDto> Items, int TotalCount)> GetMaintenanceAsync(
        MoldMaintenanceReportQuery query, DateOnly today, ReportPage? page, CancellationToken cancellationToken);

    Task<MoldReportLookupsDto> GetLookupsAsync(CancellationToken cancellationToken);
}
