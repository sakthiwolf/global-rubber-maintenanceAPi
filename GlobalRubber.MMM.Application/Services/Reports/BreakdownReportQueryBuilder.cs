using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Domain.Constants;
using GlobalRubber.MMM.Domain.Entities;

namespace GlobalRubber.MMM.Application.Services.Reports;

/// <summary>
/// The Breakdown Analysis grouping as IQueryable compositions (translated to one SQL GROUP BY by EF Core, run in memory by
/// the tests). The breakdown rows themselves - filters, sort, projection, downtime summary - are MachineReportQueryBuilder's
/// (the Breakdown Reports' list/downtime are the Machine Reports' breakdown queries, analysis 9.2 R-14/R-15). Status and
/// downtime follow the breakdown workflow: Resolved = stage Resolved or Closed; downtime = the stored downtime_hours of a
/// resolved breakdown (open ones have none).
/// </summary>
public static class BreakdownReportQueryBuilder
{
    /// <summary>Each filtered breakdown reduced to the group it belongs to and its figures.</summary>
    public static IQueryable<BreakdownGroupSource> ToGroupSource(IQueryable<MachineBreakdown> filtered, string groupBy) => groupBy switch
    {
        BreakdownReportGroupBy.Type => filtered.Select(b => new BreakdownGroupSource
        {
            Code = b.BreakdownType != null ? b.BreakdownType.BreakdownTypeCode : null,
            Name = b.BreakdownType != null ? b.BreakdownType.BreakdownTypeName : null,
            Resolved = b.Stage == BreakdownStage.Resolved || b.Stage == BreakdownStage.Closed,
            Downtime = b.Stage == BreakdownStage.Resolved || b.Stage == BreakdownStage.Closed ? b.DowntimeHours : null,
        }),
        BreakdownReportGroupBy.Department => filtered.Select(b => new BreakdownGroupSource
        {
            Code = b.Machine.Department != null ? b.Machine.Department.DepartmentCode : null, // no department -> "Unspecified"
            Name = b.Machine.Department != null ? b.Machine.Department.DepartmentName : null,
            Resolved = b.Stage == BreakdownStage.Resolved || b.Stage == BreakdownStage.Closed,
            Downtime = b.Stage == BreakdownStage.Resolved || b.Stage == BreakdownStage.Closed ? b.DowntimeHours : null,
        }),
        BreakdownReportGroupBy.Priority => filtered.Select(b => new BreakdownGroupSource
        {
            Code = b.Priority,
            Name = b.Priority,
            Resolved = b.Stage == BreakdownStage.Resolved || b.Stage == BreakdownStage.Closed,
            Downtime = b.Stage == BreakdownStage.Resolved || b.Stage == BreakdownStage.Closed ? b.DowntimeHours : null,
        }),
        BreakdownReportGroupBy.Stage => filtered.Select(b => new BreakdownGroupSource
        {
            Code = b.Stage,
            Name = b.Stage,
            Resolved = b.Stage == BreakdownStage.Resolved || b.Stage == BreakdownStage.Closed,
            Downtime = b.Stage == BreakdownStage.Resolved || b.Stage == BreakdownStage.Closed ? b.DowntimeHours : null,
        }),
        BreakdownReportGroupBy.Status => filtered.Select(b => new BreakdownGroupSource
        {
            Code = b.Stage == BreakdownStage.Resolved || b.Stage == BreakdownStage.Closed ? MachineReportBreakdownStatus.Resolved : MachineReportBreakdownStatus.Open,
            Name = b.Stage == BreakdownStage.Resolved || b.Stage == BreakdownStage.Closed ? MachineReportBreakdownStatus.Resolved : MachineReportBreakdownStatus.Open,
            Resolved = b.Stage == BreakdownStage.Resolved || b.Stage == BreakdownStage.Closed,
            Downtime = b.Stage == BreakdownStage.Resolved || b.Stage == BreakdownStage.Closed ? b.DowntimeHours : null,
        }),
        _ => filtered.Select(b => new BreakdownGroupSource
        {
            Code = b.Machine.MachineCode,
            Name = b.Machine.MachineName,
            Resolved = b.Stage == BreakdownStage.Resolved || b.Stage == BreakdownStage.Closed,
            Downtime = b.Stage == BreakdownStage.Resolved || b.Stage == BreakdownStage.Closed ? b.DowntimeHours : null,
        }),
    };

    /// <summary>One row per group (codes are unique per dimension; a null code = breakdowns without a type).</summary>
    public static IQueryable<BreakdownGroupRow> Group(IQueryable<BreakdownGroupSource> source) =>
        source
            .GroupBy(s => s.Code)
            .Select(g => new BreakdownGroupRow
            {
                Code = g.Key,
                Name = g.Max(s => s.Name),
                BreakdownCount = g.Count(),
                OpenCount = g.Count(s => !s.Resolved),
                ResolvedCount = g.Count(s => s.Resolved),
                DowntimeRecordCount = g.Count(s => s.Downtime != null),
                TotalDowntimeHours = g.Sum(s => s.Downtime ?? 0m),
                LongestDowntimeHours = g.Max(s => s.Downtime),
            });

    /// <summary>count (default, largest first) / downtime (total) / name; ties by name and code so paging is stable.</summary>
    public static IOrderedQueryable<BreakdownGroupRow> OrderGroups(IQueryable<BreakdownGroupRow> rows, MachineBreakdownReportQuery query)
    {
        var sortBy = query.SortBy ?? BreakdownReportGroupSort.Count;
        var descending = query.SortDirection is null ? sortBy != BreakdownReportGroupSort.Name : query.SortDirection == ReportSortDirection.Descending;

        var ordered = sortBy switch
        {
            BreakdownReportGroupSort.Downtime => descending ? rows.OrderByDescending(r => r.TotalDowntimeHours) : rows.OrderBy(r => r.TotalDowntimeHours),
            BreakdownReportGroupSort.Name => descending ? rows.OrderByDescending(r => r.Name) : rows.OrderBy(r => r.Name),
            _ => descending ? rows.OrderByDescending(r => r.BreakdownCount) : rows.OrderBy(r => r.BreakdownCount),
        };

        return ordered.ThenBy(r => r.Name).ThenBy(r => r.Code);
    }

    /// <summary>The group DTO: "Unspecified" for no breakdown type (R-17), average = total / downtime records.</summary>
    public static BreakdownGroupDto ToDto(BreakdownGroupRow r) => new()
    {
        Code = r.Code,
        Name = r.Name ?? BreakdownReportLabels.UnspecifiedType,
        BreakdownCount = r.BreakdownCount,
        OpenCount = r.OpenCount,
        ResolvedCount = r.ResolvedCount,
        DowntimeRecordCount = r.DowntimeRecordCount,
        TotalDowntimeHours = r.TotalDowntimeHours,
        AverageDowntimeHours = r.DowntimeRecordCount == 0
            ? null
            : Math.Round(r.TotalDowntimeHours / r.DowntimeRecordCount, 2, MidpointRounding.AwayFromZero),
        LongestDowntimeHours = r.LongestDowntimeHours,
    };
}

/// <summary>A breakdown reduced to its group key and figures.</summary>
public sealed class BreakdownGroupSource
{
    public string? Code { get; init; }
    public string? Name { get; init; }
    public bool Resolved { get; init; }
    public decimal? Downtime { get; init; }
}

/// <summary>One aggregated group, as read from the database.</summary>
public sealed class BreakdownGroupRow
{
    public string? Code { get; init; }
    public string? Name { get; init; }
    public int BreakdownCount { get; init; }
    public int OpenCount { get; init; }
    public int ResolvedCount { get; init; }
    public int DowntimeRecordCount { get; init; }
    public decimal TotalDowntimeHours { get; init; }
    public decimal? LongestDowntimeHours { get; init; }
}
