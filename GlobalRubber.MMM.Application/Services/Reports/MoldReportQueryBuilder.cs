using System.Linq.Expressions;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Domain.Constants;
using GlobalRubber.MMM.Domain.Entities;

namespace GlobalRubber.MMM.Application.Services.Reports;

/// <summary>
/// The Mold Reports' filters, sort orders, row projections and aggregates as plain IQueryable compositions - the SAME code
/// is translated to SQL by EF Core (MoldReportRepository) and run in memory by the tests (as MachineReportQueryBuilder).
/// Rows are raw database figures; the derived values (life used %, remaining life, PM state) are applied afterwards by
/// MoldReportService with the existing rules, on the page rows only.
///
/// Expects queries already validated and canonicalised by MoldReportService.
/// </summary>
public static class MoldReportQueryBuilder
{
    // ================================================================ Mold List

    public static IQueryable<Mold> FilterMolds(IQueryable<Mold> source, MoldListReportQuery query)
    {
        source = CommonMoldFilters(source, query.MoldId, query.ProductId, query.LifeState, query.Status);

        if (!string.IsNullOrEmpty(query.MoldType))
        {
            var moldType = query.MoldType;
            source = source.Where(m => m.MoldType == moldType);
        }

        if (query.IsActive is { } isActive)
            source = isActive ? source.Where(m => m.Status != MoldStatus.Retired) : source.Where(m => m.Status == MoldStatus.Retired);

        var search = Lowered(query.Search);
        if (search is not null)
        {
            source = source.Where(m =>
                m.MoldCode.ToLower().Contains(search) ||
                m.MoldName.ToLower().Contains(search) ||
                m.MoldType.ToLower().Contains(search) ||
                (m.Location != null && m.Location.ToLower().Contains(search)) ||
                m.Product.ProductCode.ToLower().Contains(search) ||
                m.Product.ProductName.ToLower().Contains(search));
        }

        return source;
    }

    public static IOrderedQueryable<Mold> OrderByCode(IQueryable<Mold> source) =>
        source.OrderBy(m => m.MoldCode).ThenBy(m => m.MoldId);

    /// <summary>The mold with its PM figures: the open shot-based PM and the last completed one (as MoldPmRepository.GetMoldUsageAsync).</summary>
    public static Expression<Func<Mold, MoldListRow>> MoldListRow(IQueryable<MoldPm> pms) => m => new MoldListRow
    {
        MoldId = m.MoldId,
        MoldCode = m.MoldCode,
        MoldName = m.MoldName,
        MoldType = m.MoldType,
        CavityCount = m.CavityCount,
        ProductCode = m.Product.ProductCode,
        ProductName = m.Product.ProductName,
        Location = m.Location,
        CurrentUsageShots = m.CurrentUsageShots,
        MaximumShots = m.MaximumShots,
        WarningShots = m.WarningShots,
        ReplacementShots = m.ReplacementShots,
        LifeState = m.LifeState,
        Status = m.Status,
        IntervalShots = m.MaintenanceFrequencyShots,
        CycleStartShots = m.PmCycleStartShots,
        PmWarningShots = m.PmWarningShots,
        LastPmDate = pms
            .Where(p => p.MoldId == m.MoldId && p.Category == MoldPmCategory.ShotBased && p.Status == MoldPmStatus.Completed)
            .OrderByDescending(p => p.CompletedDate).ThenByDescending(p => p.MoldPmId)
            .Select(p => p.CompletedDate)
            .FirstOrDefault(),
        LastPmUsage = pms
            .Where(p => p.MoldId == m.MoldId && p.Category == MoldPmCategory.ShotBased && p.Status == MoldPmStatus.Completed)
            .OrderByDescending(p => p.CompletedDate).ThenByDescending(p => p.MoldPmId)
            .Select(p => p.UsageAtCompletion)
            .FirstOrDefault(),
        OpenPmStatus = pms
            .Where(p => p.MoldId == m.MoldId && p.Category == MoldPmCategory.ShotBased && p.Status != MoldPmStatus.Completed)
            .Select(p => p.Status)
            .FirstOrDefault(),
        OpenPmScheduledDate = pms
            .Where(p => p.MoldId == m.MoldId && p.Category == MoldPmCategory.ShotBased && p.Status != MoldPmStatus.Completed)
            .Select(p => (DateOnly?)p.ScheduledDate)
            .FirstOrDefault(),
    };

    // ================================================================ Usage

    /// <summary>Saved production entries in the query's range and on its machine (the usage figures' source).</summary>
    public static IQueryable<ProductionEntry> ScopedProduction(IQueryable<ProductionEntry> entries, MoldUsageReportQuery query)
    {
        entries = entries.Where(e => e.Status == ProductionEntryStatus.Saved);

        if (query.FromDate is { } from)
            entries = entries.Where(e => e.EntryDate >= from);

        if (query.ToDate is { } to)
            entries = entries.Where(e => e.EntryDate <= to);

        if (query.MachineId is { } machineId)
            entries = entries.Where(e => e.MachineId == machineId);

        return entries;
    }

    /// <summary>
    /// With a date range or machine, only molds that have scoped production are listed; otherwise every mold.
    /// <paramref name="scoped"/> is <see cref="ScopedProduction"/>.
    /// </summary>
    public static IQueryable<Mold> FilterUsage(IQueryable<Mold> source, IQueryable<ProductionEntry> scoped, MoldUsageReportQuery query)
    {
        source = CommonMoldFilters(source, query.MoldId, productId: null, query.LifeState, status: null);

        if (query.FromDate is not null || query.ToDate is not null || query.MachineId is not null)
            source = source.Where(m => scoped.Any(e => e.MoldId == m.MoldId));

        var search = Lowered(query.Search);
        if (search is not null)
        {
            source = source.Where(m =>
                m.MoldCode.ToLower().Contains(search) ||
                m.MoldName.ToLower().Contains(search) ||
                m.Product.ProductCode.ToLower().Contains(search) ||
                m.Product.ProductName.ToLower().Contains(search));
        }

        return source;
    }

    public static Expression<Func<Mold, MoldUsageRow>> MoldUsageRow(IQueryable<ProductionEntry> scoped) => m => new MoldUsageRow
    {
        MoldId = m.MoldId,
        MoldCode = m.MoldCode,
        MoldName = m.MoldName,
        MoldType = m.MoldType,
        CavityCount = m.CavityCount,
        ProductCode = m.Product.ProductCode,
        ProductName = m.Product.ProductName,
        Location = m.Location,
        CurrentUsageShots = m.CurrentUsageShots,
        MaximumShots = m.MaximumShots,
        WarningShots = m.WarningShots,
        ReplacementShots = m.ReplacementShots,
        LifeState = m.LifeState,
        Status = m.Status,
        RangeShots = scoped.Where(e => e.MoldId == m.MoldId).Sum(e => (long?)e.ProductionQty) ?? 0L,
        RangeEntryCount = scoped.Count(e => e.MoldId == m.MoldId),
        LastProductionDate = scoped.Where(e => e.MoldId == m.MoldId)
            .OrderByDescending(e => e.EntryDate).ThenByDescending(e => e.ProductionEntryId)
            .Select(e => (DateOnly?)e.EntryDate)
            .FirstOrDefault(),
        LastMachineCode = scoped.Where(e => e.MoldId == m.MoldId)
            .OrderByDescending(e => e.EntryDate).ThenByDescending(e => e.ProductionEntryId)
            .Select(e => e.Machine.MachineCode)
            .FirstOrDefault(),
        LastMachineName = scoped.Where(e => e.MoldId == m.MoldId)
            .OrderByDescending(e => e.EntryDate).ThenByDescending(e => e.ProductionEntryId)
            .Select(e => e.Machine.MachineName)
            .FirstOrDefault(),
    };

    /// <summary>code (default, asc) / usage / rangeShots / lifeUsed; ties by mold code so paging is stable.</summary>
    public static IOrderedQueryable<MoldUsageRow> OrderUsage(IQueryable<MoldUsageRow> rows, MoldUsageReportQuery query)
    {
        var sortBy = query.SortBy ?? MoldReportSort.Code;
        var descending = Descending(query.SortDirection, sortBy);

        var ordered = sortBy switch
        {
            MoldReportSort.Usage => By(rows, r => r.CurrentUsageShots, descending),
            MoldReportSort.RangeShots => By(rows, r => r.RangeShots, descending),
            MoldReportSort.LifeUsed => By(rows, r => (double)r.CurrentUsageShots / r.MaximumShots, descending),
            _ => By(rows, r => r.MoldCode, descending),
        };

        return ordered.ThenBy(r => r.MoldCode).ThenBy(r => r.MoldId);
    }

    // ================================================================ Life Status / Replacement

    /// <summary><paramref name="replacementScope"/> limits to molds that need replacement or are Retired.</summary>
    public static IQueryable<Mold> FilterLife(IQueryable<Mold> source, MoldLifeReportQuery query, bool replacementScope)
    {
        source = CommonMoldFilters(source, query.MoldId, query.ProductId, query.LifeState, query.Status);

        if (replacementScope)
        {
            source = source.Where(m => m.Status == MoldStatus.Retired
                || m.LifeState == MoldLifeState.Replace || m.Status == MoldStatus.ReplacementDue);

            switch (query.ReplacementStatus)
            {
                case MoldReplacementStatus.Retired:
                    source = source.Where(m => m.Status == MoldStatus.Retired);
                    break;
                case MoldReplacementStatus.ReplacementRequired:
                    source = source.Where(m => m.Status != MoldStatus.Retired);
                    break;
            }
        }

        var search = Lowered(query.Search);
        if (search is not null)
        {
            source = source.Where(m =>
                m.MoldCode.ToLower().Contains(search) ||
                m.MoldName.ToLower().Contains(search) ||
                m.MoldType.ToLower().Contains(search) ||
                m.Product.ProductCode.ToLower().Contains(search) ||
                m.Product.ProductName.ToLower().Contains(search));
        }

        return source;
    }

    /// <summary>lifeUsed (default, desc = most used first) / remaining / code; ties by mold code.</summary>
    public static IOrderedQueryable<Mold> OrderLife(IQueryable<Mold> source, MoldLifeReportQuery query)
    {
        var sortBy = query.SortBy ?? MoldReportSort.LifeUsed;
        var descending = Descending(query.SortDirection, sortBy);

        var ordered = sortBy switch
        {
            MoldReportSort.Remaining => By(source, m => m.MaximumShots - m.CurrentUsageShots, descending),
            MoldReportSort.Code => By(source, m => m.MoldCode, descending),
            _ => By(source, m => (double)m.CurrentUsageShots / m.MaximumShots, descending),
        };

        return ordered.ThenBy(m => m.MoldCode).ThenBy(m => m.MoldId);
    }

    public static readonly Expression<Func<Mold, MoldBaseRow>> MoldBaseRow = m => new MoldBaseRow
    {
        MoldId = m.MoldId,
        MoldCode = m.MoldCode,
        MoldName = m.MoldName,
        MoldType = m.MoldType,
        CavityCount = m.CavityCount,
        ProductCode = m.Product.ProductCode,
        ProductName = m.Product.ProductName,
        Location = m.Location,
        CurrentUsageShots = m.CurrentUsageShots,
        MaximumShots = m.MaximumShots,
        WarningShots = m.WarningShots,
        ReplacementShots = m.ReplacementShots,
        LifeState = m.LifeState,
        Status = m.Status,
    };

    /// <summary>One-row aggregate over the filtered molds (a single GROUP BY query); no row when the set is empty.</summary>
    public static IQueryable<MoldLifeSummaryDto> LifeSummary(IQueryable<Mold> filtered) =>
        filtered
            .GroupBy(m => 1)
            .Select(g => new MoldLifeSummaryDto
            {
                TotalMolds = g.Count(),
                NormalCount = g.Count(m => m.LifeState == MoldLifeState.Normal),
                WarningCount = g.Count(m => m.LifeState == MoldLifeState.Warning),
                ReplaceCount = g.Count(m => m.LifeState == MoldLifeState.Replace),
                ReplacementRequiredCount = g.Count(m => m.Status != MoldStatus.Retired
                    && (m.LifeState == MoldLifeState.Replace || m.Status == MoldStatus.ReplacementDue)),
                RetiredCount = g.Count(m => m.Status == MoldStatus.Retired),
            });

    // ================================================================ Maintenance

    /// <param name="today">Plant (IST) date - Due vs Overdue exactly as the Mold PM page (MoldPmRepository buckets).</param>
    public static IQueryable<MoldPm> FilterMaintenance(IQueryable<MoldPm> source, MoldMaintenanceReportQuery query, DateOnly today)
    {
        if (query.MoldId is { } moldId)
            source = source.Where(p => p.MoldId == moldId);

        if (!string.IsNullOrEmpty(query.Category))
        {
            var category = query.Category;
            source = source.Where(p => p.Category == category);
        }

        source = query.Status switch
        {
            MoldPmBucket.Due => source.Where(p => p.Status == MoldPmStatus.Scheduled && p.ScheduledDate >= today),
            MoldPmBucket.Overdue => source.Where(p => p.Status == MoldPmStatus.Scheduled && p.ScheduledDate < today),
            MoldPmBucket.InProgress => source.Where(p => p.Status == MoldPmStatus.InProgress),
            MoldPmBucket.Completed => source.Where(p => p.Status == MoldPmStatus.Completed),
            _ => source,
        };

        if (query.FromDate is { } from)
            source = source.Where(p => (p.CompletedDate ?? p.ScheduledDate) >= from);

        if (query.ToDate is { } to)
            source = source.Where(p => (p.CompletedDate ?? p.ScheduledDate) <= to);

        var search = Lowered(query.Search);
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

    /// <summary>Newest maintenance first (maintenance date, then newest record).</summary>
    public static IOrderedQueryable<MoldPm> OrderMaintenance(IQueryable<MoldPm> source) =>
        source.OrderByDescending(p => p.CompletedDate ?? p.ScheduledDate).ThenByDescending(p => p.MoldPmId);

    public static Expression<Func<MoldPm, MoldMaintenanceReportItemDto>> MaintenanceRow(DateOnly today) => p => new MoldMaintenanceReportItemDto
    {
        MoldPmId = p.MoldPmId,
        PmNo = p.PmNo,
        MoldCode = p.Mold.MoldCode,
        MoldName = p.Mold.MoldName,
        Category = p.Category,
        ScheduledDate = p.ScheduledDate,
        CompletedDate = p.CompletedDate,
        Status = p.Status == MoldPmStatus.Scheduled
            ? (p.ScheduledDate < today ? MoldPmState.Overdue : MoldPmState.Due)
            : p.Status,
        IsOverdue = p.Status == MoldPmStatus.Scheduled && p.ScheduledDate < today,
        ThresholdShots = p.ThresholdShots,
        IntervalShots = p.IntervalShots,
        UsageAtTrigger = p.MoldUsageAtService,
        UsageAtCompletion = p.UsageAtCompletion,
        MaintenanceBy = p.MaintenanceBy,
        Remarks = p.Remarks,
    };

    // ================================================================ helpers

    private static IQueryable<Mold> CommonMoldFilters(IQueryable<Mold> source, int? moldId, int? productId, string? lifeState, string? status)
    {
        if (moldId is { } id)
            source = source.Where(m => m.MoldId == id);

        if (productId is { } pid)
            source = source.Where(m => m.ProductId == pid);

        if (!string.IsNullOrEmpty(lifeState))
            source = source.Where(m => m.LifeState == lifeState);

        if (!string.IsNullOrEmpty(status))
            source = source.Where(m => m.Status == status);

        return source;
    }

    /// <summary>Explicit direction wins; otherwise code sorts ascending and figures descending (largest first).</summary>
    private static bool Descending(string? direction, string sortBy) =>
        direction is null ? sortBy != MoldReportSort.Code : direction == ReportSortDirection.Descending;

    private static string? Lowered(string? search)
    {
        var trimmed = search?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed.ToLower();
    }

    private static IOrderedQueryable<T> By<T, TKey>(IQueryable<T> source, Expression<Func<T, TKey>> key, bool descending) =>
        descending ? source.OrderByDescending(key) : source.OrderBy(key);
}

/// <summary>A mold's raw master figures, as read from the database.</summary>
public class MoldBaseRow
{
    public int MoldId { get; init; }
    public string MoldCode { get; init; } = string.Empty;
    public string MoldName { get; init; } = string.Empty;
    public string MoldType { get; init; } = string.Empty;
    public int CavityCount { get; init; }
    public string ProductCode { get; init; } = string.Empty;
    public string ProductName { get; init; } = string.Empty;
    public string? Location { get; init; }
    public int CurrentUsageShots { get; init; }
    public int MaximumShots { get; init; }
    public int WarningShots { get; init; }
    public int ReplacementShots { get; init; }
    public string LifeState { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
}

/// <summary>A mold with the columns MoldPmRules needs (Mold List).</summary>
public sealed class MoldListRow : MoldBaseRow
{
    public int? IntervalShots { get; init; }
    public int CycleStartShots { get; init; }
    public int? PmWarningShots { get; init; }
    public DateOnly? LastPmDate { get; init; }
    public int? LastPmUsage { get; init; }
    public string? OpenPmStatus { get; init; }
    public DateOnly? OpenPmScheduledDate { get; init; }
}

/// <summary>A mold with its production in the report range (Usage).</summary>
public sealed class MoldUsageRow : MoldBaseRow
{
    public long RangeShots { get; init; }
    public int RangeEntryCount { get; init; }
    public DateOnly? LastProductionDate { get; init; }
    public string? LastMachineCode { get; init; }
    public string? LastMachineName { get; init; }
}
