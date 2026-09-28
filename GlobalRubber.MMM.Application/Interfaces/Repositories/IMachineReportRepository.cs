using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;

namespace GlobalRubber.MMM.Application.Interfaces.Repositories;

/// <summary>
/// Read-only data access for the Machine Reports. Every method filters, sorts, pages and projects in the database
/// (untracked) using MachineReportQueryBuilder. <c>page</c> = null returns the whole filtered set (export).
/// Nothing here writes.
/// </summary>
public interface IMachineReportRepository
{
    Task<(IReadOnlyList<MachineListReportItemDto> Items, int TotalCount)> GetMachineListAsync(
        MachineListReportQuery query, ReportPage? page, CancellationToken cancellationToken);

    /// <param name="today">Plant (IST) date - decides Scheduled vs Overdue.</param>
    Task<(IReadOnlyList<MachineMaintenanceHistoryReportItemDto> Items, int TotalCount)> GetMaintenanceHistoryAsync(
        MachineMaintenanceHistoryReportQuery query, DateOnly today, ReportPage? page, CancellationToken cancellationToken);

    Task<(IReadOnlyList<MachineBreakdownReportItemDto> Items, int TotalCount)> GetBreakdownsAsync(
        MachineBreakdownReportQuery query, ReportPage? page, CancellationToken cancellationToken);

    /// <summary>Downtime figures over the whole filtered set (not just one page).</summary>
    Task<MachineDowntimeSummaryDto> GetDowntimeSummaryAsync(
        MachineBreakdownReportQuery query, CancellationToken cancellationToken);

    Task<MachineReportLookupsDto> GetLookupsAsync(CancellationToken cancellationToken);
}

/// <summary>1-based page number and page size (already clamped by PaginationRequest).</summary>
public sealed record ReportPage(int PageNumber, int PageSize)
{
    public int Skip => (PageNumber - 1) * PageSize;
}
