using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;

namespace GlobalRubber.MMM.Application.Interfaces;

/// <summary>
/// Maintenance Reports (read-only) over Machine PMs and Mold PMs: PM Schedule, Completed, Overdue and Maintenance History,
/// each paged for the screen and exportable as CSV with the same filters and sort. Invalid filters are a
/// ValidationException (400).
/// </summary>
public interface IMaintenanceReportService
{
    Task<PagedResult<MaintenanceReportItemDto>> GetScheduleAsync(MaintenanceReportQuery query, CancellationToken cancellationToken);
    Task<PagedResult<MaintenanceReportItemDto>> GetCompletedAsync(MaintenanceReportQuery query, CancellationToken cancellationToken);
    Task<MaintenanceOverdueReportDto> GetOverdueAsync(MaintenanceReportQuery query, CancellationToken cancellationToken);
    Task<PagedResult<MaintenanceReportItemDto>> GetHistoryAsync(MaintenanceReportQuery query, CancellationToken cancellationToken);
    Task<MaintenanceReportLookupsDto> GetLookupsAsync(CancellationToken cancellationToken);

    Task<ReportFile> ExportScheduleAsync(MaintenanceReportQuery query, CancellationToken cancellationToken);
    Task<ReportFile> ExportCompletedAsync(MaintenanceReportQuery query, CancellationToken cancellationToken);
    Task<ReportFile> ExportOverdueAsync(MaintenanceReportQuery query, CancellationToken cancellationToken);
    Task<ReportFile> ExportHistoryAsync(MaintenanceReportQuery query, CancellationToken cancellationToken);
}
