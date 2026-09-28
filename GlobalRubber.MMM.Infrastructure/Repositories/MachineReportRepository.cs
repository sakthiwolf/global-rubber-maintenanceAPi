using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Application.Services.Reports;
using GlobalRubber.MMM.Domain.Constants;
using GlobalRubber.MMM.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GlobalRubber.MMM.Infrastructure.Repositories;

/// <summary>
/// Read-only Machine Reports queries. Untracked; filters/sorts/projections come from MachineReportQueryBuilder and are
/// translated to SQL, so only the requested page (or, for an export, the filtered set) and only the DTO columns leave
/// the database - one query per page (plus one COUNT), no per-row lookups.
/// </summary>
public sealed class MachineReportRepository : IMachineReportRepository
{
    private readonly GlobalRubberDbContext _dbContext;

    public MachineReportRepository(GlobalRubberDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<(IReadOnlyList<MachineListReportItemDto> Items, int TotalCount)> GetMachineListAsync(
        MachineListReportQuery query, ReportPage? page, CancellationToken cancellationToken)
    {
        var filtered = MachineReportQueryBuilder.FilterMachines(_dbContext.Machines.AsNoTracking(), query);
        var ordered = MachineReportQueryBuilder.OrderMachines(filtered);

        return await ReportPaging.PageAsync(filtered, ordered.Select(MachineReportQueryBuilder.MachineRow), page, cancellationToken);
    }

    public async Task<(IReadOnlyList<MachineMaintenanceHistoryReportItemDto> Items, int TotalCount)> GetMaintenanceHistoryAsync(
        MachineMaintenanceHistoryReportQuery query, DateOnly today, ReportPage? page, CancellationToken cancellationToken)
    {
        var filtered = MachineReportQueryBuilder.FilterMaintenanceHistory(_dbContext.MachinePms.AsNoTracking(), query, today);
        var ordered = MachineReportQueryBuilder.OrderMaintenanceHistory(filtered);

        return await ReportPaging.PageAsync(filtered, ordered.Select(MachineReportQueryBuilder.MaintenanceHistoryRow(today)), page, cancellationToken);
    }

    public async Task<(IReadOnlyList<MachineBreakdownReportItemDto> Items, int TotalCount)> GetBreakdownsAsync(
        MachineBreakdownReportQuery query, ReportPage? page, CancellationToken cancellationToken)
    {
        var filtered = MachineReportQueryBuilder.FilterBreakdowns(_dbContext.MachineBreakdowns.AsNoTracking(), query);
        var ordered = MachineReportQueryBuilder.OrderBreakdowns(filtered, query);

        return await ReportPaging.PageAsync(filtered, ordered.Select(MachineReportQueryBuilder.BreakdownRow), page, cancellationToken);
    }

    public async Task<MachineDowntimeSummaryDto> GetDowntimeSummaryAsync(
        MachineBreakdownReportQuery query, CancellationToken cancellationToken)
    {
        var filtered = MachineReportQueryBuilder.FilterBreakdowns(_dbContext.MachineBreakdowns.AsNoTracking(), query);

        var aggregate = await MachineReportQueryBuilder.DowntimeAggregate(filtered).FirstOrDefaultAsync(cancellationToken);
        var longest = aggregate is { DowntimeRecordCount: > 0 }
            ? await MachineReportQueryBuilder.LongestDowntimeBreakdownNo(filtered).FirstOrDefaultAsync(cancellationToken)
            : null;

        return MachineReportQueryBuilder.ToSummary(aggregate, longest);
    }

    public async Task<MachineReportLookupsDto> GetLookupsAsync(CancellationToken cancellationToken)
    {
        var machines = await _dbContext.Machines.AsNoTracking()
            .OrderBy(m => m.MachineCode)
            .Select(m => new ReportLookupItemDto { Id = m.MachineId, Code = m.MachineCode, Name = m.MachineName, IsActive = m.IsActive })
            .ToListAsync(cancellationToken);

        var departments = await _dbContext.Departments.AsNoTracking()
            .Where(d => _dbContext.Machines.Any(m => m.DepartmentId == d.DepartmentId))
            .OrderBy(d => d.DepartmentName)
            .Select(d => new ReportLookupItemDto { Id = d.DepartmentId, Code = d.DepartmentCode, Name = d.DepartmentName, IsActive = d.IsActive })
            .ToListAsync(cancellationToken);

        var machineTypes = await _dbContext.Machines.AsNoTracking()
            .Select(m => m.MachineType)
            .Distinct()
            .OrderBy(t => t)
            .ToListAsync(cancellationToken);

        var breakdownTypes = await _dbContext.BreakdownTypes.AsNoTracking()
            .OrderBy(t => t.BreakdownTypeName)
            .Select(t => new ReportLookupItemDto { Id = t.BreakdownTypeId, Code = t.BreakdownTypeCode, Name = t.BreakdownTypeName, IsActive = t.IsActive })
            .ToListAsync(cancellationToken);

        var maintenanceTypes = await _dbContext.MaintenanceTypes.AsNoTracking()
            .Where(t => t.AppliesTo == MaintenanceTypeAppliesTo.Machine || t.AppliesTo == MaintenanceTypeAppliesTo.Both)
            .OrderBy(t => t.MaintenanceTypeName)
            .Select(t => new ReportLookupItemDto { Id = t.MaintenanceTypeId, Code = t.MaintenanceTypeCode, Name = t.MaintenanceTypeName, IsActive = t.IsActive })
            .ToListAsync(cancellationToken);

        var plans = await _dbContext.MaintenanceChecklists.AsNoTracking()
            .Where(c => c.AppliesTo == MaintenanceChecklistAppliesTo.Machine)
            .OrderBy(c => c.ChecklistCode)
            .Select(c => new ReportLookupItemDto { Id = c.ChecklistId, Code = c.ChecklistCode, Name = c.ChecklistName, IsActive = c.IsActive })
            .ToListAsync(cancellationToken);

        return new MachineReportLookupsDto
        {
            Machines = machines,
            Departments = departments,
            MachineTypes = machineTypes,
            BreakdownTypes = breakdownTypes,
            MaintenanceTypes = maintenanceTypes,
            MaintenancePlans = plans,
            Criticalities = MachineCriticality.All,
            OperationalStatuses = MachineOperationalStatus.All,
            Priorities = BreakdownPriority.All,
            Stages = BreakdownStage.All,
            MaintenanceStatuses = MachineReportPmStatus.All,
            BreakdownStatuses = MachineReportBreakdownStatus.All,
        };
    }
}
