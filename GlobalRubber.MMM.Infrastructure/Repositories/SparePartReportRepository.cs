using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Application.Services.Reports;
using GlobalRubber.MMM.Domain.Constants;
using GlobalRubber.MMM.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GlobalRubber.MMM.Infrastructure.Repositories;

/// <summary>
/// Read-only Spare Part Reports queries. Untracked; filters/sorts/projections come from SparePartReportQueryBuilder and
/// are translated to SQL (correlated sub-queries for the latest ledger balance and user names - no per-row round trips).
/// </summary>
public sealed class SparePartReportRepository : ISparePartReportRepository
{
    private readonly GlobalRubberDbContext _dbContext;

    public SparePartReportRepository(GlobalRubberDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<(IReadOnlyList<SparePartStockItemDto> Items, int TotalCount)> GetStockAsync(
        SparePartStockReportQuery query, SparePartStockScope scope, ReportPage? page, CancellationToken cancellationToken)
    {
        var filtered = SparePartReportQueryBuilder.FilterParts(_dbContext.SpareParts.AsNoTracking(), query, scope);
        var rows = SparePartReportQueryBuilder.OrderParts(filtered, query, scope)
            .Select(SparePartReportQueryBuilder.StockRow(_dbContext.SparePartStockTransactions.AsNoTracking()));

        return ReportPaging.PageAsync(filtered, rows, page, cancellationToken);
    }

    public async Task<SparePartLowStockSummaryDto> GetLowStockSummaryAsync(SparePartStockReportQuery query, CancellationToken cancellationToken)
    {
        var filtered = SparePartReportQueryBuilder.FilterParts(_dbContext.SpareParts.AsNoTracking(), query, SparePartStockScope.LowStock);
        return await SparePartReportQueryBuilder.LowStockSummary(filtered).FirstOrDefaultAsync(cancellationToken) ?? new SparePartLowStockSummaryDto();
    }

    public async Task<SparePartValuationSummaryDto> GetValuationSummaryAsync(SparePartStockReportQuery query, CancellationToken cancellationToken)
    {
        var filtered = SparePartReportQueryBuilder.FilterParts(_dbContext.SpareParts.AsNoTracking(), query, SparePartStockScope.Valuation);
        return await SparePartReportQueryBuilder.ValuationSummary(filtered).FirstOrDefaultAsync(cancellationToken) ?? new SparePartValuationSummaryDto();
    }

    public Task<(IReadOnlyList<SparePartMovementItemDto> Items, int TotalCount)> GetMovementsAsync(
        SparePartMovementReportQuery query, ReportPage? page, CancellationToken cancellationToken)
    {
        var ledger = SparePartReportQueryBuilder.FilterLedger(_dbContext.SparePartStockTransactions.AsNoTracking(), query);
        var rows = SparePartReportQueryBuilder.MovementRows(ledger, _dbContext.SpareParts.AsNoTracking(), _dbContext.Users.AsNoTracking(), query);
        var items = SparePartReportQueryBuilder.OrderMovements(rows, query).Select(SparePartReportQueryBuilder.MovementItem);

        return ReportPaging.PageAsync(rows, items, page, cancellationToken);
    }

    public Task<(IReadOnlyList<SparePartUsageItemDto> Items, int TotalCount)> GetUsageAsync(
        SparePartUsageReportQuery query, ReportPage? page, CancellationToken cancellationToken)
    {
        var filtered = SparePartReportQueryBuilder.FilterUsage(_dbContext.SparePartUsages.AsNoTracking(), query);
        var rows = SparePartReportQueryBuilder.OrderUsage(filtered, query).Select(SparePartReportQueryBuilder.UsageRow(_dbContext.Users.AsNoTracking()));

        return ReportPaging.PageAsync(filtered, rows, page, cancellationToken);
    }

    public async Task<SparePartReportLookupsDto> GetLookupsAsync(CancellationToken cancellationToken)
    {
        var parts = await _dbContext.SpareParts.AsNoTracking()
            .OrderBy(p => p.SparePartCode)
            .Select(p => new ReportLookupItemDto { Id = p.SparePartId, Code = p.SparePartCode, Name = p.SparePartName, IsActive = p.IsActive })
            .ToListAsync(cancellationToken);

        var machines = await _dbContext.Machines.AsNoTracking()
            .Where(m => _dbContext.SpareParts.Any(p => p.MachineId == m.MachineId) || _dbContext.SparePartUsages.Any(u => u.MachineId == m.MachineId))
            .OrderBy(m => m.MachineCode)
            .Select(m => new ReportLookupItemDto { Id = m.MachineId, Code = m.MachineCode, Name = m.MachineName, IsActive = m.IsActive })
            .ToListAsync(cancellationToken);

        var molds = await _dbContext.Molds.AsNoTracking()
            .Where(m => _dbContext.SparePartUsages.Any(u => u.MoldId == m.MoldId))
            .OrderBy(m => m.MoldCode)
            .Select(m => new ReportLookupItemDto { Id = m.MoldId, Code = m.MoldCode, Name = m.MoldName, IsActive = m.Status != MoldStatus.Retired })
            .ToListAsync(cancellationToken);

        var vendors = await _dbContext.Vendors.AsNoTracking()
            .Where(v => _dbContext.SpareParts.Any(p => p.VendorId == v.VendorId))
            .OrderBy(v => v.VendorName)
            .Select(v => new ReportLookupItemDto { Id = v.VendorId, Code = v.VendorCode, Name = v.VendorName, IsActive = v.IsActive })
            .ToListAsync(cancellationToken);

        var categories = await _dbContext.SpareParts.AsNoTracking()
            .Where(p => p.Category != null && p.Category != "")
            .Select(p => p.Category!)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync(cancellationToken);

        return new SparePartReportLookupsDto
        {
            SpareParts = parts,
            Machines = machines,
            Molds = molds,
            Vendors = vendors,
            Categories = categories,
            StockStatuses = SparePartStockStatus.All,
            AttentionStatuses = SparePartReportLists.AttentionStatuses,
            TransactionTypes = SparePartReportLists.TransactionTypes,
            Directions = SparePartMovementDirection.All,
            UsageStatuses = SparePartReportLists.UsageStatuses,
            UsedFor = SparePartReportLists.UsedFor,
        };
    }
}
