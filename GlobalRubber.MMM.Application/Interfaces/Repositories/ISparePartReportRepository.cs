using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Services.Reports;

namespace GlobalRubber.MMM.Application.Interfaces.Repositories;

/// <summary>
/// Read-only data access for the Spare Part Reports: filters, sorts, pages and aggregates in the database (untracked) with
/// SparePartReportQueryBuilder. <c>page</c> = null returns the whole filtered set (export). Nothing here writes - the
/// stock, ledger and notifications stay the Spare Part / Spare Part Usage workflows' business.
/// </summary>
public interface ISparePartReportRepository
{
    Task<(IReadOnlyList<SparePartStockItemDto> Items, int TotalCount)> GetStockAsync(
        SparePartStockReportQuery query, SparePartStockScope scope, ReportPage? page, CancellationToken cancellationToken);

    Task<SparePartLowStockSummaryDto> GetLowStockSummaryAsync(SparePartStockReportQuery query, CancellationToken cancellationToken);

    Task<SparePartValuationSummaryDto> GetValuationSummaryAsync(SparePartStockReportQuery query, CancellationToken cancellationToken);

    Task<(IReadOnlyList<SparePartMovementItemDto> Items, int TotalCount)> GetMovementsAsync(
        SparePartMovementReportQuery query, ReportPage? page, CancellationToken cancellationToken);

    Task<(IReadOnlyList<SparePartUsageItemDto> Items, int TotalCount)> GetUsageAsync(
        SparePartUsageReportQuery query, ReportPage? page, CancellationToken cancellationToken);

    Task<SparePartReportLookupsDto> GetLookupsAsync(CancellationToken cancellationToken);
}
