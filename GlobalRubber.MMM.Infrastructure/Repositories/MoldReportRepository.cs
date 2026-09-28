using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Application.Services.Reports;
using GlobalRubber.MMM.Domain.Constants;
using GlobalRubber.MMM.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GlobalRubber.MMM.Infrastructure.Repositories;

/// <summary>
/// Read-only Mold Reports queries. Untracked; filters/sorts/projections come from MoldReportQueryBuilder and are translated
/// to SQL (correlated sub-queries for the PM and production figures - no per-row round trips).
/// </summary>
public sealed class MoldReportRepository : IMoldReportRepository
{
    private readonly GlobalRubberDbContext _dbContext;

    public MoldReportRepository(GlobalRubberDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<(IReadOnlyList<MoldListRow> Items, int TotalCount)> GetMoldListAsync(
        MoldListReportQuery query, ReportPage? page, CancellationToken cancellationToken)
    {
        var filtered = MoldReportQueryBuilder.FilterMolds(_dbContext.Molds.AsNoTracking(), query);
        var rows = MoldReportQueryBuilder.OrderByCode(filtered)
            .Select(MoldReportQueryBuilder.MoldListRow(_dbContext.MoldPms.AsNoTracking()));

        return ReportPaging.PageAsync(filtered, rows, page, cancellationToken);
    }

    public Task<(IReadOnlyList<MoldUsageRow> Items, int TotalCount)> GetUsageAsync(
        MoldUsageReportQuery query, ReportPage? page, CancellationToken cancellationToken)
    {
        var scoped = MoldReportQueryBuilder.ScopedProduction(_dbContext.ProductionEntries.AsNoTracking(), query);
        var filtered = MoldReportQueryBuilder.FilterUsage(_dbContext.Molds.AsNoTracking(), scoped, query);
        var rows = MoldReportQueryBuilder.OrderUsage(filtered.Select(MoldReportQueryBuilder.MoldUsageRow(scoped)), query);

        return ReportPaging.PageAsync(filtered, rows, page, cancellationToken);
    }

    public Task<(IReadOnlyList<MoldBaseRow> Items, int TotalCount)> GetLifeAsync(
        MoldLifeReportQuery query, bool replacementScope, ReportPage? page, CancellationToken cancellationToken)
    {
        var filtered = MoldReportQueryBuilder.FilterLife(_dbContext.Molds.AsNoTracking(), query, replacementScope);
        var rows = MoldReportQueryBuilder.OrderLife(filtered, query).Select(MoldReportQueryBuilder.MoldBaseRow);

        return ReportPaging.PageAsync(filtered, rows, page, cancellationToken);
    }

    public async Task<MoldLifeSummaryDto> GetLifeSummaryAsync(
        MoldLifeReportQuery query, bool replacementScope, CancellationToken cancellationToken)
    {
        var filtered = MoldReportQueryBuilder.FilterLife(_dbContext.Molds.AsNoTracking(), query, replacementScope);
        return await MoldReportQueryBuilder.LifeSummary(filtered).FirstOrDefaultAsync(cancellationToken) ?? new MoldLifeSummaryDto();
    }

    public Task<(IReadOnlyList<MoldMaintenanceReportItemDto> Items, int TotalCount)> GetMaintenanceAsync(
        MoldMaintenanceReportQuery query, DateOnly today, ReportPage? page, CancellationToken cancellationToken)
    {
        var filtered = MoldReportQueryBuilder.FilterMaintenance(_dbContext.MoldPms.AsNoTracking(), query, today);
        var rows = MoldReportQueryBuilder.OrderMaintenance(filtered).Select(MoldReportQueryBuilder.MaintenanceRow(today));

        return ReportPaging.PageAsync(filtered, rows, page, cancellationToken);
    }

    public async Task<MoldReportLookupsDto> GetLookupsAsync(CancellationToken cancellationToken)
    {
        var molds = await _dbContext.Molds.AsNoTracking()
            .OrderBy(m => m.MoldCode)
            .Select(m => new ReportLookupItemDto { Id = m.MoldId, Code = m.MoldCode, Name = m.MoldName, IsActive = m.Status != MoldStatus.Retired })
            .ToListAsync(cancellationToken);

        var products = await _dbContext.Products.AsNoTracking()
            .Where(p => _dbContext.Molds.Any(m => m.ProductId == p.ProductId))
            .OrderBy(p => p.ProductName)
            .Select(p => new ReportLookupItemDto { Id = p.ProductId, Code = p.ProductCode, Name = p.ProductName, IsActive = p.IsActive })
            .ToListAsync(cancellationToken);

        var machines = await _dbContext.Machines.AsNoTracking()
            .Where(mc => _dbContext.ProductionEntries.Any(e => e.MachineId == mc.MachineId))
            .OrderBy(mc => mc.MachineCode)
            .Select(mc => new ReportLookupItemDto { Id = mc.MachineId, Code = mc.MachineCode, Name = mc.MachineName, IsActive = mc.IsActive })
            .ToListAsync(cancellationToken);

        var moldTypes = await _dbContext.Molds.AsNoTracking()
            .Select(m => m.MoldType).Distinct().OrderBy(t => t)
            .ToListAsync(cancellationToken);

        return new MoldReportLookupsDto
        {
            Molds = molds,
            Products = products,
            Machines = machines,
            MoldTypes = moldTypes,
            Statuses = MoldStatus.All,
            LifeStates = MoldLifeState.All,
            Categories = MoldPmCategory.All,
            MaintenanceStatuses = MoldPmBucket.All,
            ReplacementStatuses = MoldReplacementStatus.All,
        };
    }
}
