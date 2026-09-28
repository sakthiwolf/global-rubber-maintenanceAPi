using System.Linq.Expressions;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Domain.Common;
using GlobalRubber.MMM.Domain.Constants;
using GlobalRubber.MMM.Domain.Entities;

namespace GlobalRubber.MMM.Application.Services.Reports;

/// <summary>
/// The Machine Reports' filters, sort orders and row projections as plain IQueryable compositions, so the SAME code is
/// translated to SQL by EF Core (MachineReportRepository) and run in memory by the tests. Everything here is
/// expression-only (no client evaluation): filtering, sorting, paging and aggregation all happen in the database.
///
/// Expects queries already validated and canonicalised by MachineReportService (known status values, sort keys, etc.).
/// </summary>
public static class MachineReportQueryBuilder
{
    // Stored timestamps (assigned_at, resolved_at, ...) are UTC; reports show plant (IST) local time.
    private static readonly double PlantOffsetMinutes = PlantTime.UtcOffset.TotalMinutes;

    // ================================================================ Machine List

    public static IQueryable<Machine> FilterMachines(IQueryable<Machine> source, MachineListReportQuery query)
    {
        if (query.MachineId is { } machineId)
            source = source.Where(m => m.MachineId == machineId);

        if (query.DepartmentId is { } departmentId)
            source = source.Where(m => m.DepartmentId == departmentId);

        if (!string.IsNullOrEmpty(query.MachineType))
        {
            var machineType = query.MachineType;
            source = source.Where(m => m.MachineType == machineType);
        }

        if (!string.IsNullOrEmpty(query.OperationalStatus))
        {
            var status = query.OperationalStatus;
            source = source.Where(m => m.OperationalStatus == status);
        }

        if (!string.IsNullOrEmpty(query.Criticality))
        {
            var criticality = query.Criticality;
            source = source.Where(m => m.Criticality == criticality);
        }

        if (query.IsActive is { } isActive)
            source = source.Where(m => m.IsActive == isActive);

        if (query.NextMaintenanceFrom is { } nextFrom)
            source = source.Where(m => m.NextMaintenanceDate != null && m.NextMaintenanceDate >= nextFrom);

        if (query.NextMaintenanceTo is { } nextTo)
            source = source.Where(m => m.NextMaintenanceDate != null && m.NextMaintenanceDate <= nextTo);

        var search = Lowered(query.Search);
        if (search is not null)
        {
            source = source.Where(m =>
                m.MachineCode.ToLower().Contains(search) ||
                m.MachineName.ToLower().Contains(search) ||
                m.MachineType.ToLower().Contains(search) ||
                m.Location.ToLower().Contains(search) ||
                m.Department.DepartmentName.ToLower().Contains(search));
        }

        return source;
    }

    /// <summary>Machine code order (the Machine master's natural order).</summary>
    public static IOrderedQueryable<Machine> OrderMachines(IQueryable<Machine> source) =>
        source.OrderBy(m => m.MachineCode).ThenBy(m => m.MachineId);

    public static readonly Expression<Func<Machine, MachineListReportItemDto>> MachineRow = m => new MachineListReportItemDto
    {
        MachineId = m.MachineId,
        MachineCode = m.MachineCode,
        MachineName = m.MachineName,
        MachineType = m.MachineType,
        DepartmentName = m.Department.DepartmentName,
        Location = m.Location,
        Criticality = m.Criticality,
        OperationalStatus = m.OperationalStatus,
        IsActive = m.IsActive,
        LastMaintenanceDate = m.LastMaintenanceDate,
        NextMaintenanceDate = m.NextMaintenanceDate,
    };

    // ================================================================ Maintenance History

    /// <param name="today">The plant (IST) date - decides Scheduled vs Overdue exactly as the Machine PM module does.</param>
    public static IQueryable<MachinePm> FilterMaintenanceHistory(
        IQueryable<MachinePm> source, MachineMaintenanceHistoryReportQuery query, DateOnly today)
    {
        if (query.MachineId is { } machineId)
            source = source.Where(pm => pm.MachineId == machineId);

        if (query.MaintenanceTypeId is { } maintenanceTypeId)
            source = source.Where(pm => pm.MaintenanceTypeId == maintenanceTypeId);

        if (query.ChecklistId is { } checklistId)
            source = source.Where(pm => pm.ChecklistId == checklistId);

        switch (query.Status)
        {
            case MachineReportPmStatus.Completed:
                source = source.Where(pm => pm.Status == MachinePmStatus.Completed);
                break;
            case MachineReportPmStatus.Scheduled:
                source = source.Where(pm => pm.Status != MachinePmStatus.Completed && pm.ScheduledDate >= today);
                break;
            case MachineReportPmStatus.Overdue:
                source = source.Where(pm => pm.Status != MachinePmStatus.Completed && pm.ScheduledDate < today);
                break;
        }

        // Maintenance date = completed date when completed, otherwise the due date.
        if (query.FromDate is { } from)
            source = source.Where(pm => (pm.CompletedDate ?? pm.ScheduledDate) >= from);

        if (query.ToDate is { } to)
            source = source.Where(pm => (pm.CompletedDate ?? pm.ScheduledDate) <= to);

        var search = Lowered(query.Search);
        if (search is not null)
        {
            source = source.Where(pm =>
                pm.PmNo.ToLower().Contains(search) ||
                pm.Machine.MachineCode.ToLower().Contains(search) ||
                pm.Machine.MachineName.ToLower().Contains(search) ||
                (pm.Checklist != null && (pm.Checklist.ChecklistCode.ToLower().Contains(search) ||
                                          pm.Checklist.ChecklistName.ToLower().Contains(search))) ||
                (pm.MaintenanceBy != null && pm.MaintenanceBy.ToLower().Contains(search)) ||
                (pm.Remarks != null && pm.Remarks.ToLower().Contains(search)));
        }

        return source;
    }

    /// <summary>Newest maintenance first (maintenance date, then newest record).</summary>
    public static IOrderedQueryable<MachinePm> OrderMaintenanceHistory(IQueryable<MachinePm> source) =>
        source.OrderByDescending(pm => pm.CompletedDate ?? pm.ScheduledDate).ThenByDescending(pm => pm.MachinePmId);

    public static Expression<Func<MachinePm, MachineMaintenanceHistoryReportItemDto>> MaintenanceHistoryRow(DateOnly today) =>
        pm => new MachineMaintenanceHistoryReportItemDto
        {
            MachinePmId = pm.MachinePmId,
            PmNo = pm.PmNo,
            MachineCode = pm.Machine.MachineCode,
            MachineName = pm.Machine.MachineName,
            ChecklistCode = pm.Checklist != null ? pm.Checklist.ChecklistCode : null,
            ChecklistName = pm.Checklist != null ? pm.Checklist.ChecklistName : null,
            Frequency = pm.Checklist != null ? pm.Checklist.Frequency : null,
            MaintenanceTypeName = pm.MaintenanceType != null ? pm.MaintenanceType.MaintenanceTypeName : null,
            ScheduledDate = pm.ScheduledDate,
            CompletedDate = pm.CompletedDate,
            Status = pm.Status != MachinePmStatus.Completed && pm.ScheduledDate < today
                ? MachineReportPmStatus.Overdue
                : pm.Status,
            IsOverdue = pm.Status != MachinePmStatus.Completed && pm.ScheduledDate < today,
            MaintenanceBy = pm.MaintenanceBy,
            ChecklistItemsDone = pm.ChecklistItems.Count(i => i.IsChecked),
            ChecklistItemsTotal = pm.ChecklistItems.Count(),
            Remarks = pm.Remarks,
        };

    // ================================================================ Breakdown / Downtime

    public static IQueryable<MachineBreakdown> FilterBreakdowns(IQueryable<MachineBreakdown> source, MachineBreakdownReportQuery query)
    {
        if (query.MachineId is { } machineId)
            source = source.Where(b => b.MachineId == machineId);

        if (query.BreakdownTypeId is { } breakdownTypeId)
            source = source.Where(b => b.BreakdownTypeId == breakdownTypeId);

        if (query.DepartmentId is { } departmentId)
            source = source.Where(b => b.Machine.DepartmentId == departmentId);

        var reportedBy = Lowered(query.ReportedBy);
        if (reportedBy is not null)
            source = source.Where(b => b.ReportedBy != null && b.ReportedBy.ToLower().Contains(reportedBy));

        if (!string.IsNullOrEmpty(query.Priority))
        {
            var priority = query.Priority;
            source = source.Where(b => b.Priority == priority);
        }

        if (!string.IsNullOrEmpty(query.Stage))
        {
            var stage = query.Stage;
            source = source.Where(b => b.Stage == stage);
        }

        switch (query.Status)
        {
            case MachineReportBreakdownStatus.Resolved:
                source = source.Where(b => b.Stage == BreakdownStage.Resolved || b.Stage == BreakdownStage.Closed);
                break;
            case MachineReportBreakdownStatus.Open:
                source = source.Where(b => b.Stage != BreakdownStage.Resolved && b.Stage != BreakdownStage.Closed);
                break;
        }

        if (query.FromDate is { } from)
            source = source.Where(b => b.BreakdownDate >= from);

        if (query.ToDate is { } to)
            source = source.Where(b => b.BreakdownDate <= to);

        var search = Lowered(query.Search);
        if (search is not null)
        {
            source = source.Where(b =>
                b.BreakdownNo.ToLower().Contains(search) ||
                b.Machine.MachineCode.ToLower().Contains(search) ||
                b.Machine.MachineName.ToLower().Contains(search) ||
                b.Problem.ToLower().Contains(search) ||
                (b.ReportedBy != null && b.ReportedBy.ToLower().Contains(search)) ||
                (b.AssignedEngineer != null && b.AssignedEngineer.EmployeeName.ToLower().Contains(search)) ||
                (b.BreakdownType != null && b.BreakdownType.BreakdownTypeName.ToLower().Contains(search)));
        }

        return source;
    }

    /// <summary>
    /// Sort by date (breakdown date + time), machine code, duration (stored downtime; open breakdowns have none),
    /// priority (Low &lt; Medium &lt; High &lt; Critical) or status (Open &lt; Resolved, then stage order). Ties fall back to the breakdown date/time and number so paging is
    /// stable. Default: date, newest first.
    /// </summary>
    public static IOrderedQueryable<MachineBreakdown> OrderBreakdowns(IQueryable<MachineBreakdown> source, MachineBreakdownReportQuery query)
    {
        var descending = query.SortDirection != ReportSortDirection.Ascending;

        IOrderedQueryable<MachineBreakdown> ordered = query.SortBy switch
        {
            MachineReportBreakdownSort.Machine => By(source, b => b.Machine.MachineCode, descending),
            MachineReportBreakdownSort.Duration => By(source,
                b => b.Stage == BreakdownStage.Resolved || b.Stage == BreakdownStage.Closed ? b.DowntimeHours : null, descending),
            MachineReportBreakdownSort.Priority => By(source,
                b => b.Priority == BreakdownPriority.Critical ? 4
                    : b.Priority == BreakdownPriority.High ? 3
                    : b.Priority == BreakdownPriority.Medium ? 2
                    : 1, descending),
            MachineReportBreakdownSort.Status => By(source,
                b => b.Stage == BreakdownStage.Reported ? 1
                    : b.Stage == BreakdownStage.Assigned ? 2
                    : b.Stage == BreakdownStage.MaintenanceStarted ? 3
                    : b.Stage == BreakdownStage.Resolved ? 4
                    : 5, descending),
            _ => By(source, b => b.BreakdownDate, descending),
        };

        return ThenBy(ThenBy(ThenBy(ordered, b => b.BreakdownDate, descending), b => b.BreakdownTime, descending),
            b => b.MachineBreakdownId, descending);
    }

    public static readonly Expression<Func<MachineBreakdown, MachineBreakdownReportItemDto>> BreakdownRow = b => new MachineBreakdownReportItemDto
    {
        MachineBreakdownId = b.MachineBreakdownId,
        BreakdownNo = b.BreakdownNo,
        MachineCode = b.Machine.MachineCode,
        MachineName = b.Machine.MachineName,
        DepartmentName = b.Machine.Department.DepartmentName,
        BreakdownDate = b.BreakdownDate,
        BreakdownTime = b.BreakdownTime,
        Problem = b.Problem,
        Description = b.Description,
        BreakdownTypeName = b.BreakdownType != null ? b.BreakdownType.BreakdownTypeName : null,
        Priority = b.Priority,
        Stage = b.Stage,
        Status = b.Stage == BreakdownStage.Resolved || b.Stage == BreakdownStage.Closed
            ? MachineReportBreakdownStatus.Resolved
            : MachineReportBreakdownStatus.Open,
        ReportedBy = b.ReportedBy,
        AssignedEngineerName = b.AssignedEngineer != null ? b.AssignedEngineer.EmployeeName : null,
        AssignedAt = b.AssignedAt != null ? b.AssignedAt.Value.AddMinutes(PlantOffsetMinutes) : null,
        MaintenanceStartedAt = b.MaintenanceStartedAt != null ? b.MaintenanceStartedAt.Value.AddMinutes(PlantOffsetMinutes) : null,
        ResolvedAt = b.ResolvedAt != null ? b.ResolvedAt.Value.AddMinutes(PlantOffsetMinutes) : null,
        ClosedAt = b.ClosedAt != null ? b.ClosedAt.Value.AddMinutes(PlantOffsetMinutes) : null,
        RootCause = b.RootCause,
        CorrectiveAction = b.CorrectiveAction,
        // An open breakdown has no downtime yet (user decision 2026-09-26: shown as Ongoing, no elapsed figure).
        DowntimeHours = b.Stage == BreakdownStage.Resolved || b.Stage == BreakdownStage.Closed ? b.DowntimeHours : null,
    };

    /// <summary>
    /// One-row aggregate over the filtered breakdowns (a single GROUP BY query). Yields no row when the set is empty.
    /// </summary>
    public static IQueryable<DowntimeAggregate> DowntimeAggregate(IQueryable<MachineBreakdown> filtered) =>
        filtered
            .GroupBy(b => 1)
            .Select(g => new DowntimeAggregate
            {
                BreakdownCount = g.Count(),
                ResolvedCount = g.Count(b => b.Stage == BreakdownStage.Resolved || b.Stage == BreakdownStage.Closed),
                DowntimeRecordCount = g.Count(b => (b.Stage == BreakdownStage.Resolved || b.Stage == BreakdownStage.Closed) && b.DowntimeHours != null),
                TotalDowntimeHours = g.Sum(b => (b.Stage == BreakdownStage.Resolved || b.Stage == BreakdownStage.Closed) && b.DowntimeHours != null
                    ? b.DowntimeHours!.Value
                    : 0m),
                LongestDowntimeHours = g.Max(b => b.Stage == BreakdownStage.Resolved || b.Stage == BreakdownStage.Closed ? b.DowntimeHours : null),
            });

    /// <summary>The breakdown with the longest stored downtime (latest first on a tie).</summary>
    public static IQueryable<string> LongestDowntimeBreakdownNo(IQueryable<MachineBreakdown> filtered) =>
        filtered
            .Where(b => (b.Stage == BreakdownStage.Resolved || b.Stage == BreakdownStage.Closed) && b.DowntimeHours != null)
            .OrderByDescending(b => b.DowntimeHours)
            .ThenByDescending(b => b.BreakdownDate)
            .ThenByDescending(b => b.MachineBreakdownId)
            .Select(b => b.BreakdownNo);

    /// <summary>Builds the summary DTO from the aggregate row (null = no breakdowns matched).</summary>
    public static MachineDowntimeSummaryDto ToSummary(DowntimeAggregate? aggregate, string? longestBreakdownNo)
    {
        if (aggregate is null)
            return new MachineDowntimeSummaryDto();

        return new MachineDowntimeSummaryDto
        {
            BreakdownCount = aggregate.BreakdownCount,
            ResolvedCount = aggregate.ResolvedCount,
            OpenCount = aggregate.BreakdownCount - aggregate.ResolvedCount,
            DowntimeRecordCount = aggregate.DowntimeRecordCount,
            TotalDowntimeHours = aggregate.TotalDowntimeHours,
            AverageDowntimeHours = aggregate.DowntimeRecordCount == 0
                ? null
                : Math.Round(aggregate.TotalDowntimeHours / aggregate.DowntimeRecordCount, 2, MidpointRounding.AwayFromZero),
            LongestDowntimeHours = aggregate.LongestDowntimeHours,
            LongestDowntimeBreakdownNo = aggregate.DowntimeRecordCount == 0 ? null : longestBreakdownNo,
        };
    }

    // ================================================================ helpers

    private static string? Lowered(string? search)
    {
        var trimmed = search?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed.ToLower();
    }

    private static IOrderedQueryable<T> By<T, TKey>(IQueryable<T> source, Expression<Func<T, TKey>> key, bool descending) =>
        descending ? source.OrderByDescending(key) : source.OrderBy(key);

    private static IOrderedQueryable<T> ThenBy<T, TKey>(IOrderedQueryable<T> source, Expression<Func<T, TKey>> key, bool descending) =>
        descending ? source.ThenByDescending(key) : source.ThenBy(key);
}

/// <summary>The raw downtime aggregate row (see MachineReportQueryBuilder.DowntimeAggregate).</summary>
public sealed class DowntimeAggregate
{
    public int BreakdownCount { get; init; }
    public int ResolvedCount { get; init; }
    public int DowntimeRecordCount { get; init; }
    public decimal TotalDowntimeHours { get; init; }
    public decimal? LongestDowntimeHours { get; init; }
}
