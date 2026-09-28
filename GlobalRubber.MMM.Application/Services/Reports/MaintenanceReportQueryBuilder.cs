using System.Linq.Expressions;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Domain.Constants;
using GlobalRubber.MMM.Domain.Entities;

namespace GlobalRubber.MMM.Application.Services.Reports;

/// <summary>Which Maintenance Reports tab a query is for (decides the base set, the date the range filters and the default sort).</summary>
public enum MaintenanceReportScope
{
    /// <summary>Open PMs (not completed) - due date.</summary>
    Schedule,

    /// <summary>Completed PMs - completed date.</summary>
    Completed,

    /// <summary>Currently overdue PMs, each by its own workflow's rule - due date.</summary>
    Overdue,

    /// <summary>Every PM - maintenance date (completed date, else due date).</summary>
    History,
}

/// <summary>
/// The Maintenance Reports' filters, status rules, projections and sort orders as IQueryable compositions - translated to
/// SQL by EF Core (MaintenanceReportRepository, one UNION ALL of the two PM workflows) and run in memory by the tests.
///
/// The status/overdue rules are the EXISTING ones, written as expressions:
///   Machine PM - overdue = status &lt;&gt; Completed AND due date &lt; today (MachinePmService: MachinePmDto.IsOverdue, D-03)
///   Mold PM    - overdue = status = Scheduled AND due date &lt; today (MoldPmService: MoldPmRules.IsOverdue); Scheduled and
///                not overdue = Due; In Progress; Completed (MoldPmRepository buckets due/overdue/in-progress/completed)
/// Expects queries validated and canonicalised by MaintenanceReportService.
/// </summary>
public static class MaintenanceReportQueryBuilder
{
    // ================================================================ which workflows a query can return

    /// <summary>False when a filter can only match Mold PMs (asset type Mold, a mold, a category, status Due).</summary>
    public static bool IncludesMachinePms(MaintenanceReportQuery q) =>
        q.AssetType != MaintenanceReportAssetType.Mold && q.MoldId is null && q.Category is null && q.Status != MaintenanceReportStatus.Due;

    /// <summary>False when a filter can only match Machine PMs (asset type Machine, a machine, a plan, a maintenance type, status Scheduled).</summary>
    public static bool IncludesMoldPms(MaintenanceReportQuery q) =>
        q.AssetType != MaintenanceReportAssetType.Machine && q.MachineId is null && q.ChecklistId is null
        && q.MaintenanceTypeId is null && q.Status != MaintenanceReportStatus.Scheduled;

    // ================================================================ Machine PM

    public static IQueryable<MachinePm> FilterMachinePms(IQueryable<MachinePm> source, MaintenanceReportQuery q, MaintenanceReportScope scope, DateOnly today)
    {
        source = scope switch
        {
            MaintenanceReportScope.Schedule => source.Where(pm => pm.Status != MachinePmStatus.Completed),
            MaintenanceReportScope.Completed => source.Where(pm => pm.Status == MachinePmStatus.Completed),
            MaintenanceReportScope.Overdue => source.Where(pm => pm.Status != MachinePmStatus.Completed && pm.ScheduledDate < today),
            _ => source,
        };

        source = q.Status switch
        {
            MaintenanceReportStatus.Scheduled => source.Where(pm => pm.Status == MachinePmStatus.Scheduled && pm.ScheduledDate >= today),
            MaintenanceReportStatus.InProgress => source.Where(pm => pm.Status == MachinePmStatus.InProgress && pm.ScheduledDate >= today),
            MaintenanceReportStatus.Overdue => source.Where(pm => pm.Status != MachinePmStatus.Completed && pm.ScheduledDate < today),
            MaintenanceReportStatus.Completed => source.Where(pm => pm.Status == MachinePmStatus.Completed),
            _ => source,
        };

        if (q.MachineId is { } machineId)
            source = source.Where(pm => pm.MachineId == machineId);

        if (q.ChecklistId is { } checklistId)
            source = source.Where(pm => pm.ChecklistId == checklistId);

        if (q.MaintenanceTypeId is { } typeId)
            source = source.Where(pm => pm.MaintenanceTypeId == typeId);

        var performedBy = Lowered(q.PerformedBy);
        if (performedBy is not null)
            source = source.Where(pm => pm.MaintenanceBy != null && pm.MaintenanceBy.ToLower().Contains(performedBy));

        if (q.FromDate is { } from)
        {
            source = scope switch
            {
                MaintenanceReportScope.Completed => source.Where(pm => pm.CompletedDate >= from),
                MaintenanceReportScope.History => source.Where(pm => (pm.CompletedDate ?? pm.ScheduledDate) >= from),
                _ => source.Where(pm => pm.ScheduledDate >= from),
            };
        }

        if (q.ToDate is { } to)
        {
            source = scope switch
            {
                MaintenanceReportScope.Completed => source.Where(pm => pm.CompletedDate <= to),
                MaintenanceReportScope.History => source.Where(pm => (pm.CompletedDate ?? pm.ScheduledDate) <= to),
                _ => source.Where(pm => pm.ScheduledDate <= to),
            };
        }

        var search = Lowered(q.Search);
        if (search is not null)
        {
            source = source.Where(pm =>
                pm.PmNo.ToLower().Contains(search) ||
                pm.Machine.MachineCode.ToLower().Contains(search) ||
                pm.Machine.MachineName.ToLower().Contains(search) ||
                (pm.Checklist != null && (pm.Checklist.ChecklistCode.ToLower().Contains(search) || pm.Checklist.ChecklistName.ToLower().Contains(search))) ||
                (pm.MaintenanceBy != null && pm.MaintenanceBy.ToLower().Contains(search)) ||
                (pm.Remarks != null && pm.Remarks.ToLower().Contains(search)));
        }

        return source;
    }

    public static Expression<Func<MachinePm, MaintenanceReportRow>> MachinePmRow(DateOnly today) => pm => new MaintenanceReportRow
    {
        Source = MaintenanceReportSource.MachinePm,
        AssetType = MaintenanceReportAssetType.Machine,
        RecordId = pm.MachinePmId,
        PmNo = pm.PmNo,
        AssetCode = pm.Machine.MachineCode,
        AssetName = pm.Machine.MachineName,
        Category = null,
        PlanCode = pm.Checklist != null ? pm.Checklist.ChecklistCode : null,
        PlanName = pm.Checklist != null ? pm.Checklist.ChecklistName : null,
        Frequency = pm.Checklist != null ? pm.Checklist.Frequency : null,
        MaintenanceTypeName = pm.MaintenanceType != null ? pm.MaintenanceType.MaintenanceTypeName : null,
        ScheduledDate = pm.ScheduledDate,
        CompletedDate = pm.CompletedDate,
        MaintenanceDate = pm.CompletedDate ?? pm.ScheduledDate,
        Status = pm.Status == MachinePmStatus.Completed
            ? MaintenanceReportStatus.Completed
            : pm.ScheduledDate < today ? MaintenanceReportStatus.Overdue : pm.Status,
        PerformedBy = pm.MaintenanceBy,
        CurrentUsageShots = null,
        ThresholdShots = null,
        IntervalShots = null,
        UsageAtTrigger = null,
        UsageAtCompletion = null,
        ChecklistItemsDone = pm.ChecklistItems.Count(i => i.IsChecked),
        ChecklistItemsTotal = pm.ChecklistItems.Count(),
        Remarks = pm.Remarks,
    };

    // ================================================================ Mold PM

    public static IQueryable<MoldPm> FilterMoldPms(IQueryable<MoldPm> source, MaintenanceReportQuery q, MaintenanceReportScope scope, DateOnly today)
    {
        source = scope switch
        {
            MaintenanceReportScope.Schedule => source.Where(p => p.Status != MoldPmStatus.Completed),
            MaintenanceReportScope.Completed => source.Where(p => p.Status == MoldPmStatus.Completed),
            MaintenanceReportScope.Overdue => source.Where(p => p.Status == MoldPmStatus.Scheduled && p.ScheduledDate < today),
            _ => source,
        };

        source = q.Status switch
        {
            MaintenanceReportStatus.Due => source.Where(p => p.Status == MoldPmStatus.Scheduled && p.ScheduledDate >= today),
            MaintenanceReportStatus.Overdue => source.Where(p => p.Status == MoldPmStatus.Scheduled && p.ScheduledDate < today),
            MaintenanceReportStatus.InProgress => source.Where(p => p.Status == MoldPmStatus.InProgress),
            MaintenanceReportStatus.Completed => source.Where(p => p.Status == MoldPmStatus.Completed),
            _ => source,
        };

        if (q.MoldId is { } moldId)
            source = source.Where(p => p.MoldId == moldId);

        if (q.Category is { } category)
            source = source.Where(p => p.Category == category);

        var performedBy = Lowered(q.PerformedBy);
        if (performedBy is not null)
            source = source.Where(p => p.MaintenanceBy != null && p.MaintenanceBy.ToLower().Contains(performedBy));

        if (q.FromDate is { } from)
        {
            source = scope switch
            {
                MaintenanceReportScope.Completed => source.Where(p => p.CompletedDate >= from),
                MaintenanceReportScope.History => source.Where(p => (p.CompletedDate ?? p.ScheduledDate) >= from),
                _ => source.Where(p => p.ScheduledDate >= from),
            };
        }

        if (q.ToDate is { } to)
        {
            source = scope switch
            {
                MaintenanceReportScope.Completed => source.Where(p => p.CompletedDate <= to),
                MaintenanceReportScope.History => source.Where(p => (p.CompletedDate ?? p.ScheduledDate) <= to),
                _ => source.Where(p => p.ScheduledDate <= to),
            };
        }

        var search = Lowered(q.Search);
        if (search is not null)
        {
            source = source.Where(p =>
                p.PmNo.ToLower().Contains(search) ||
                p.Mold.MoldCode.ToLower().Contains(search) ||
                p.Mold.MoldName.ToLower().Contains(search) ||
                (p.MaintenanceBy != null && p.MaintenanceBy.ToLower().Contains(search)) ||
                (p.Remarks != null && p.Remarks.ToLower().Contains(search)));
        }

        return source;
    }

    public static Expression<Func<MoldPm, MaintenanceReportRow>> MoldPmRow(DateOnly today) => p => new MaintenanceReportRow
    {
        Source = MaintenanceReportSource.MoldPm,
        AssetType = MaintenanceReportAssetType.Mold,
        RecordId = p.MoldPmId,
        PmNo = p.PmNo,
        AssetCode = p.Mold.MoldCode,
        AssetName = p.Mold.MoldName,
        Category = p.Category,
        PlanCode = null,
        PlanName = null,
        Frequency = null,
        MaintenanceTypeName = null,
        ScheduledDate = p.ScheduledDate,
        CompletedDate = p.CompletedDate,
        MaintenanceDate = p.CompletedDate ?? p.ScheduledDate,
        Status = p.Status == MoldPmStatus.Completed
            ? MaintenanceReportStatus.Completed
            : p.Status == MoldPmStatus.InProgress
                ? MaintenanceReportStatus.InProgress
                : p.ScheduledDate < today ? MaintenanceReportStatus.Overdue : MaintenanceReportStatus.Due,
        PerformedBy = p.MaintenanceBy,
        CurrentUsageShots = p.Mold.CurrentUsageShots,
        ThresholdShots = p.ThresholdShots,
        IntervalShots = p.IntervalShots,
        UsageAtTrigger = p.MoldUsageAtService,
        UsageAtCompletion = p.UsageAtCompletion,
        ChecklistItemsDone = null,
        ChecklistItemsTotal = null,
        Remarks = p.Remarks,
    };

    // ================================================================ sort (over the combined rows)

    /// <summary>
    /// date (default) / asset / source / status. The date is the tab's own date; default direction: schedule and overdue
    /// earliest first (most overdue first), completed and history newest first. Ties: date, source, record id - stable paging.
    /// </summary>
    public static IOrderedQueryable<MaintenanceReportRow> Order(IQueryable<MaintenanceReportRow> rows, MaintenanceReportQuery q, MaintenanceReportScope scope)
    {
        var descending = q.SortDirection is null
            ? scope is MaintenanceReportScope.Completed or MaintenanceReportScope.History
            : q.SortDirection == ReportSortDirection.Descending;

        Expression<Func<MaintenanceReportRow, DateOnly?>> date = scope switch
        {
            MaintenanceReportScope.Completed => r => r.CompletedDate,
            MaintenanceReportScope.History => r => r.MaintenanceDate,
            _ => r => r.ScheduledDate,
        };

        var ordered = q.SortBy switch
        {
            MaintenanceReportSort.Asset => By(rows, r => r.AssetCode, descending),
            MaintenanceReportSort.Source => By(rows, r => r.Source, descending),
            MaintenanceReportSort.Status => By(rows, r => r.Status, descending),
            _ => By(rows, date, descending),
        };

        return (descending ? ordered.ThenByDescending(date) : ordered.ThenBy(date))
            .ThenBy(r => r.Source)
            .ThenByDescending(r => r.RecordId);
    }

    // ================================================================ helpers

    private static string? Lowered(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed.ToLower();
    }

    private static IOrderedQueryable<T> By<T, TKey>(IQueryable<T> source, Expression<Func<T, TKey>> key, bool descending) =>
        descending ? source.OrderByDescending(key) : source.OrderBy(key);
}

/// <summary>A PM occurrence of either workflow in one shape (both projections assign every member in the same order).</summary>
public sealed class MaintenanceReportRow
{
    public string Source { get; init; } = string.Empty;
    public string AssetType { get; init; } = string.Empty;
    public int RecordId { get; init; }
    public string PmNo { get; init; } = string.Empty;
    public string AssetCode { get; init; } = string.Empty;
    public string AssetName { get; init; } = string.Empty;
    public string? Category { get; init; }
    public string? PlanCode { get; init; }
    public string? PlanName { get; init; }
    public string? Frequency { get; init; }
    public string? MaintenanceTypeName { get; init; }
    public DateOnly ScheduledDate { get; init; }
    public DateOnly? CompletedDate { get; init; }
    public DateOnly? MaintenanceDate { get; init; }
    public string Status { get; init; } = string.Empty;
    public string? PerformedBy { get; init; }
    public int? CurrentUsageShots { get; init; }
    public int? ThresholdShots { get; init; }
    public int? IntervalShots { get; init; }
    public int? UsageAtTrigger { get; init; }
    public int? UsageAtCompletion { get; init; }
    public int? ChecklistItemsDone { get; init; }
    public int? ChecklistItemsTotal { get; init; }
    public string? Remarks { get; init; }
}
