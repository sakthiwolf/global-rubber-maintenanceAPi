using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;

namespace GlobalRubber.MMM.Application.Interfaces;

/// <summary>
/// Spare Part Reports (read-only): Spare Part Stock, Stock Movement, Spare Part Usage, Low Stock and Stock Valuation, each
/// paged for the screen and exportable as CSV with the same filters and sort. Invalid filters are a ValidationException (400).
/// </summary>
public interface ISparePartReportService
{
    Task<PagedResult<SparePartStockItemDto>> GetStockAsync(SparePartStockReportQuery query, CancellationToken cancellationToken);
    Task<PagedResult<SparePartMovementItemDto>> GetMovementsAsync(SparePartMovementReportQuery query, CancellationToken cancellationToken);
    Task<PagedResult<SparePartUsageItemDto>> GetUsageAsync(SparePartUsageReportQuery query, CancellationToken cancellationToken);
    Task<SparePartLowStockReportDto> GetLowStockAsync(SparePartStockReportQuery query, CancellationToken cancellationToken);
    Task<SparePartValuationReportDto> GetValuationAsync(SparePartStockReportQuery query, CancellationToken cancellationToken);
    Task<SparePartReportLookupsDto> GetLookupsAsync(CancellationToken cancellationToken);

    Task<ReportFile> ExportStockAsync(SparePartStockReportQuery query, CancellationToken cancellationToken);
    Task<ReportFile> ExportMovementsAsync(SparePartMovementReportQuery query, CancellationToken cancellationToken);
    Task<ReportFile> ExportUsageAsync(SparePartUsageReportQuery query, CancellationToken cancellationToken);
    Task<ReportFile> ExportLowStockAsync(SparePartStockReportQuery query, CancellationToken cancellationToken);
    Task<ReportFile> ExportValuationAsync(SparePartStockReportQuery query, CancellationToken cancellationToken);
}
