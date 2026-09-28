using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;

namespace GlobalRubber.MMM.Application.Interfaces;

/// <summary>
/// Machine Reports (read-only): Machine List, Maintenance History, Breakdown and Downtime, each paged for the screen and
/// exportable as CSV with the same filters. Invalid filters are a ValidationException (400).
/// </summary>
public interface IMachineReportService
{
    Task<PagedResult<MachineListReportItemDto>> GetMachineListAsync(MachineListReportQuery query, CancellationToken cancellationToken);

    Task<PagedResult<MachineMaintenanceHistoryReportItemDto>> GetMaintenanceHistoryAsync(
        MachineMaintenanceHistoryReportQuery query, CancellationToken cancellationToken);

    Task<PagedResult<MachineBreakdownReportItemDto>> GetBreakdownsAsync(MachineBreakdownReportQuery query, CancellationToken cancellationToken);

    Task<MachineDowntimeReportDto> GetDowntimeAsync(MachineBreakdownReportQuery query, CancellationToken cancellationToken);

    Task<MachineReportLookupsDto> GetLookupsAsync(CancellationToken cancellationToken);

    Task<ReportFile> ExportMachineListAsync(MachineListReportQuery query, CancellationToken cancellationToken);

    Task<ReportFile> ExportMaintenanceHistoryAsync(MachineMaintenanceHistoryReportQuery query, CancellationToken cancellationToken);

    Task<ReportFile> ExportBreakdownsAsync(MachineBreakdownReportQuery query, CancellationToken cancellationToken);

    Task<ReportFile> ExportDowntimeAsync(MachineBreakdownReportQuery query, CancellationToken cancellationToken);
}
