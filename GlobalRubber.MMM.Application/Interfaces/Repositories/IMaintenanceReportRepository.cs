using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Services.Reports;

namespace GlobalRubber.MMM.Application.Interfaces.Repositories;

/// <summary>
/// Read-only data access for the Maintenance Reports: Machine PMs and Mold PMs filtered, combined (UNION ALL), sorted and
/// paged in the database with MaintenanceReportQueryBuilder. <c>page</c> = null returns the whole filtered set (export).
/// </summary>
public interface IMaintenanceReportRepository
{
    /// <param name="today">Plant (IST) date - the Due / Overdue rules.</param>
    Task<(IReadOnlyList<MaintenanceReportRow> Items, int TotalCount)> GetRowsAsync(
        MaintenanceReportQuery query, MaintenanceReportScope scope, DateOnly today, ReportPage? page, CancellationToken cancellationToken);

    /// <summary>Counts per workflow over the whole filtered overdue set.</summary>
    Task<MaintenanceOverdueSummaryDto> GetOverdueSummaryAsync(
        MaintenanceReportQuery query, DateOnly today, CancellationToken cancellationToken);

    Task<MaintenanceReportLookupsDto> GetLookupsAsync(CancellationToken cancellationToken);
}
