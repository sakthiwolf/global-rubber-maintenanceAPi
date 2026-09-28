using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;

namespace GlobalRubber.MMM.Application.Interfaces;

/// <summary>
/// Breakdown Reports (read-only): Breakdown List, Breakdown Analysis, Downtime Analysis and Breakdown History, each paged
/// for the screen and exportable as CSV with the same filters and sort. Invalid filters are a ValidationException (400).
/// </summary>
public interface IBreakdownReportService
{
    Task<PagedResult<MachineBreakdownReportItemDto>> GetListAsync(MachineBreakdownReportQuery query, CancellationToken cancellationToken);
    Task<BreakdownAnalysisReportDto> GetAnalysisAsync(BreakdownAnalysisReportQuery query, CancellationToken cancellationToken);
    Task<MachineDowntimeReportDto> GetDowntimeAsync(MachineBreakdownReportQuery query, CancellationToken cancellationToken);
    Task<PagedResult<MachineBreakdownReportItemDto>> GetHistoryAsync(MachineBreakdownReportQuery query, CancellationToken cancellationToken);
    Task<BreakdownReportLookupsDto> GetLookupsAsync(CancellationToken cancellationToken);

    Task<ReportFile> ExportListAsync(MachineBreakdownReportQuery query, CancellationToken cancellationToken);
    Task<ReportFile> ExportAnalysisAsync(BreakdownAnalysisReportQuery query, CancellationToken cancellationToken);
    Task<ReportFile> ExportDowntimeAsync(MachineBreakdownReportQuery query, CancellationToken cancellationToken);
    Task<ReportFile> ExportHistoryAsync(MachineBreakdownReportQuery query, CancellationToken cancellationToken);
}
