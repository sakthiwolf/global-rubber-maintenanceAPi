using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Application.Services.Reports;
using GlobalRubber.MMM.Domain.Constants;
using GlobalRubber.MMM.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GlobalRubber.MMM.Infrastructure.Repositories;

/// <summary>
/// Read-only Maintenance Reports queries. Each PM workflow is filtered with its own rules, projected to the common row
/// and combined with UNION ALL; sorting and paging run on the combined set in SQL. The total is the sum of the two
/// filtered COUNTs.
/// </summary>
public sealed class MaintenanceReportRepository : IMaintenanceReportRepository
{
    private readonly GlobalRubberDbContext _dbContext;

    public MaintenanceReportRepository(GlobalRubberDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<(IReadOnlyList<MaintenanceReportRow> Items, int TotalCount)> GetRowsAsync(
        MaintenanceReportQuery query, MaintenanceReportScope scope, DateOnly today, ReportPage? page, CancellationToken cancellationToken)
    {
        var machine = MaintenanceReportQueryBuilder.IncludesMachinePms(query)
            ? MaintenanceReportQueryBuilder.FilterMachinePms(_dbContext.MachinePms.AsNoTracking(), query, scope, today)
            : null;
        var mold = MaintenanceReportQueryBuilder.IncludesMoldPms(query)
            ? MaintenanceReportQueryBuilder.FilterMoldPms(_dbContext.MoldPms.AsNoTracking(), query, scope, today)
            : null;

        var rows = Combine(machine?.Select(MaintenanceReportQueryBuilder.MachinePmRow(today)), mold?.Select(MaintenanceReportQueryBuilder.MoldPmRow(today)));
        if (rows is null)
            return (Array.Empty<MaintenanceReportRow>(), 0);

        var ordered = MaintenanceReportQueryBuilder.Order(rows, query, scope);
        if (page is null)
        {
            var all = await ordered.ToListAsync(cancellationToken);
            return (all, all.Count);
        }

        var total = (machine is null ? 0 : await machine.CountAsync(cancellationToken))
                    + (mold is null ? 0 : await mold.CountAsync(cancellationToken));
        var items = total == 0
            ? new List<MaintenanceReportRow>()
            : await ordered.Skip(page.Skip).Take(page.PageSize).ToListAsync(cancellationToken);

        return (items, total);
    }

    public async Task<MaintenanceOverdueSummaryDto> GetOverdueSummaryAsync(
        MaintenanceReportQuery query, DateOnly today, CancellationToken cancellationToken)
    {
        var machine = MaintenanceReportQueryBuilder.IncludesMachinePms(query)
            ? await MaintenanceReportQueryBuilder.FilterMachinePms(_dbContext.MachinePms.AsNoTracking(), query, MaintenanceReportScope.Overdue, today).CountAsync(cancellationToken)
            : 0;
        var mold = MaintenanceReportQueryBuilder.IncludesMoldPms(query)
            ? await MaintenanceReportQueryBuilder.FilterMoldPms(_dbContext.MoldPms.AsNoTracking(), query, MaintenanceReportScope.Overdue, today).CountAsync(cancellationToken)
            : 0;

        return new MaintenanceOverdueSummaryDto { TotalOverdue = machine + mold, MachinePmOverdue = machine, MoldPmOverdue = mold };
    }

    public async Task<MaintenanceReportLookupsDto> GetLookupsAsync(CancellationToken cancellationToken)
    {
        var machines = await _dbContext.Machines.AsNoTracking()
            .OrderBy(m => m.MachineCode)
            .Select(m => new ReportLookupItemDto { Id = m.MachineId, Code = m.MachineCode, Name = m.MachineName, IsActive = m.IsActive })
            .ToListAsync(cancellationToken);

        var molds = await _dbContext.Molds.AsNoTracking()
            .OrderBy(m => m.MoldCode)
            .Select(m => new ReportLookupItemDto { Id = m.MoldId, Code = m.MoldCode, Name = m.MoldName, IsActive = m.Status != MoldStatus.Retired })
            .ToListAsync(cancellationToken);

        var plans = await _dbContext.MaintenanceChecklists.AsNoTracking()
            .Where(c => c.AppliesTo == MaintenanceChecklistAppliesTo.Machine && c.Frequency != null) // plans only, never checklist masters (migration 020)
            .OrderBy(c => c.ChecklistCode)
            .Select(c => new ReportLookupItemDto { Id = c.ChecklistId, Code = c.ChecklistCode, Name = c.ChecklistName, IsActive = c.IsActive })
            .ToListAsync(cancellationToken);

        var types = await _dbContext.MaintenanceTypes.AsNoTracking()
            .Where(t => t.AppliesTo == MaintenanceTypeAppliesTo.Machine || t.AppliesTo == MaintenanceTypeAppliesTo.Both)
            .OrderBy(t => t.MaintenanceTypeName)
            .Select(t => new ReportLookupItemDto { Id = t.MaintenanceTypeId, Code = t.MaintenanceTypeCode, Name = t.MaintenanceTypeName, IsActive = t.IsActive })
            .ToListAsync(cancellationToken);

        return new MaintenanceReportLookupsDto
        {
            Machines = machines,
            Molds = molds,
            MaintenancePlans = plans,
            MaintenanceTypes = types,
            Categories = MoldPmCategory.All,
            AssetTypes = MaintenanceReportAssetType.All,
            OpenStatuses = MaintenanceReportStatus.Open,
            Statuses = MaintenanceReportStatus.All,
        };
    }

    private static IQueryable<MaintenanceReportRow>? Combine(IQueryable<MaintenanceReportRow>? machine, IQueryable<MaintenanceReportRow>? mold) =>
        machine is null ? mold : mold is null ? machine : machine.Concat(mold);
}
