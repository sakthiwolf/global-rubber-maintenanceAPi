using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;

namespace GlobalRubber.MMM.Application.Interfaces;

/// <summary>
/// Mold Reports (read-only): Mold List, Usage, Life Status, Maintenance and Replacement, each paged for the screen and
/// exportable as CSV with the same filters. Invalid filters are a ValidationException (400).
/// </summary>
public interface IMoldReportService
{
    Task<PagedResult<MoldListReportItemDto>> GetMoldListAsync(MoldListReportQuery query, CancellationToken cancellationToken);
    Task<PagedResult<MoldUsageReportItemDto>> GetUsageAsync(MoldUsageReportQuery query, CancellationToken cancellationToken);
    Task<MoldLifeReportDto> GetLifeStatusAsync(MoldLifeReportQuery query, CancellationToken cancellationToken);
    Task<PagedResult<MoldMaintenanceReportItemDto>> GetMaintenanceAsync(MoldMaintenanceReportQuery query, CancellationToken cancellationToken);
    Task<MoldLifeReportDto> GetReplacementAsync(MoldLifeReportQuery query, CancellationToken cancellationToken);
    Task<MoldReportLookupsDto> GetLookupsAsync(CancellationToken cancellationToken);

    Task<ReportFile> ExportMoldListAsync(MoldListReportQuery query, CancellationToken cancellationToken);
    Task<ReportFile> ExportUsageAsync(MoldUsageReportQuery query, CancellationToken cancellationToken);
    Task<ReportFile> ExportLifeStatusAsync(MoldLifeReportQuery query, CancellationToken cancellationToken);
    Task<ReportFile> ExportMaintenanceAsync(MoldMaintenanceReportQuery query, CancellationToken cancellationToken);
    Task<ReportFile> ExportReplacementAsync(MoldLifeReportQuery query, CancellationToken cancellationToken);
}
