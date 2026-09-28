using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Application.Services.Reports;
using GlobalRubber.MMM.Domain.Constants;
using static GlobalRubber.MMM.Application.Common.ReportFilterValidation;

namespace GlobalRubber.MMM.Application.Services;

/// <summary>
/// Breakdown Reports - read-only, over transactions.machine_breakdown_transaction. The Breakdown List and Downtime
/// Analysis are the SAME breakdown queries the Machine Reports use (IMachineReportRepository, MachineReportQueryBuilder -
/// analysis 9.2: R-14/R-15 duplicate R-03/R-04 and "should share one API"); only the permission (RPT_BREAKDOWN) and the
/// columns differ. Existing breakdown rules, unchanged:
///   Open = Reported / Assigned / Maintenance Started; Resolved = Resolved / Closed (the stage workflow)
///   downtime = downtime_hours stored at Resolved (resolved - maintenance started); open = Ongoing, no hours, left out of
///   the downtime totals (user decision 2026-09-26)
///   timestamps shown in plant (IST) time; no breakdown type = "Unspecified" (R-17)
/// History = the resolved breakdowns (status fixed). breakdown_stage_transaction is not used: nothing writes it (0 rows).
/// No MTBF/MTTR: no operating-hours data exists to support them.
/// </summary>
public sealed class BreakdownReportService : IBreakdownReportService
{
    private readonly IMachineReportRepository _breakdowns;
    private readonly IBreakdownReportRepository _repository;
    private readonly IDateTimeProvider _dateTimeProvider;

    public BreakdownReportService(IMachineReportRepository breakdowns, IBreakdownReportRepository repository, IDateTimeProvider dateTimeProvider)
    {
        _breakdowns = breakdowns;
        _repository = repository;
        _dateTimeProvider = dateTimeProvider;
    }

    // ================================================================ Screen (paged)

    public async Task<PagedResult<MachineBreakdownReportItemDto>> GetListAsync(MachineBreakdownReportQuery query, CancellationToken cancellationToken)
    {
        Validate(query, history: false);
        var (items, total) = await _breakdowns.GetBreakdownsAsync(query, PageOf(query), cancellationToken);
        return PagedResult<MachineBreakdownReportItemDto>.Create(items, query.PageNumber, query.PageSize, total);
    }

    public async Task<MachineDowntimeReportDto> GetDowntimeAsync(MachineBreakdownReportQuery query, CancellationToken cancellationToken)
    {
        Validate(query, history: false);
        var summary = await _breakdowns.GetDowntimeSummaryAsync(query, cancellationToken);
        var (items, total) = await _breakdowns.GetBreakdownsAsync(query, PageOf(query), cancellationToken);
        return new MachineDowntimeReportDto
        {
            Summary = summary,
            Page = PagedResult<MachineBreakdownReportItemDto>.Create(items, query.PageNumber, query.PageSize, total),
        };
    }

    public async Task<PagedResult<MachineBreakdownReportItemDto>> GetHistoryAsync(MachineBreakdownReportQuery query, CancellationToken cancellationToken)
    {
        Validate(query, history: true);
        var (items, total) = await _breakdowns.GetBreakdownsAsync(query, PageOf(query), cancellationToken);
        return PagedResult<MachineBreakdownReportItemDto>.Create(items, query.PageNumber, query.PageSize, total);
    }

    public async Task<BreakdownAnalysisReportDto> GetAnalysisAsync(BreakdownAnalysisReportQuery query, CancellationToken cancellationToken)
    {
        Validate(query);
        var summary = await _breakdowns.GetDowntimeSummaryAsync(query, cancellationToken);
        var (groups, total) = await _repository.GetGroupsAsync(query, PageOf(query), cancellationToken);
        return new BreakdownAnalysisReportDto
        {
            GroupBy = query.GroupBy!,
            Summary = summary,
            Groups = PagedResult<BreakdownGroupDto>.Create(groups.Select(BreakdownReportQueryBuilder.ToDto).ToList(), query.PageNumber, query.PageSize, total),
        };
    }

    public Task<BreakdownReportLookupsDto> GetLookupsAsync(CancellationToken cancellationToken) =>
        _repository.GetLookupsAsync(cancellationToken);

    // ================================================================ Export (whole filtered, sorted set - CSV)

    public async Task<ReportFile> ExportListAsync(MachineBreakdownReportQuery query, CancellationToken cancellationToken)
    {
        Validate(query, history: false);
        var (items, _) = await _breakdowns.GetBreakdownsAsync(query, page: null, cancellationToken);

        var csv = Identity(new CsvReportWriter<MachineBreakdownReportItemDto>())
            .Column("Breakdown Date", r => Date(r.BreakdownDate))
            .Column("Breakdown Time", r => Time(r.BreakdownTime))
            .Column("Problem", r => r.Problem)
            .Column("Description", r => r.Description)
            .Column("Breakdown Type", r => r.BreakdownTypeName)
            .Column("Priority", r => r.Priority)
            .Column("Stage", r => r.Stage)
            .Column("Status", r => r.Status)
            .Column("Reported By", r => r.ReportedBy)
            .Column("Assigned To", r => r.AssignedEngineerName)
            .Column("Maintenance Started", r => DateTime(r.MaintenanceStartedAt))
            .Column("Resolved At", r => DateTime(r.ResolvedAt))
            .Column("Downtime (hrs)", r => Downtime(r))
            .Write(items);

        return File("breakdown-list-report", csv);
    }

    public async Task<ReportFile> ExportDowntimeAsync(MachineBreakdownReportQuery query, CancellationToken cancellationToken)
    {
        Validate(query, history: false);
        var (items, _) = await _breakdowns.GetBreakdownsAsync(query, page: null, cancellationToken);

        var csv = Identity(new CsvReportWriter<MachineBreakdownReportItemDto>())
            .Column("Breakdown Type", r => r.BreakdownTypeName)
            .Column("Priority", r => r.Priority)
            .Column("Breakdown Start", r => $"{Date(r.BreakdownDate)} {Time(r.BreakdownTime)}")
            .Column("Maintenance Started", r => DateTime(r.MaintenanceStartedAt))
            .Column("Resolved At", r => DateTime(r.ResolvedAt))
            .Column("Downtime (hrs)", r => Downtime(r))
            .Column("Status", r => r.Status == MachineReportBreakdownStatus.Open ? "Ongoing" : r.Status)
            .Write(items);

        return File("breakdown-downtime-report", csv);
    }

    public async Task<ReportFile> ExportHistoryAsync(MachineBreakdownReportQuery query, CancellationToken cancellationToken)
    {
        Validate(query, history: true);
        var (items, _) = await _breakdowns.GetBreakdownsAsync(query, page: null, cancellationToken);

        var csv = Identity(new CsvReportWriter<MachineBreakdownReportItemDto>())
            .Column("Breakdown Type", r => r.BreakdownTypeName)
            .Column("Priority", r => r.Priority)
            .Column("Problem", r => r.Problem)
            .Column("Reported By", r => r.ReportedBy)
            .Column("Breakdown Start", r => $"{Date(r.BreakdownDate)} {Time(r.BreakdownTime)}")
            .Column("Assigned To", r => r.AssignedEngineerName)
            .Column("Assigned At", r => DateTime(r.AssignedAt))
            .Column("Maintenance Started", r => DateTime(r.MaintenanceStartedAt))
            .Column("Resolved At", r => DateTime(r.ResolvedAt))
            .Column("Closed At", r => DateTime(r.ClosedAt))
            .Column("Stage", r => r.Stage)
            .Column("Downtime (hrs)", r => Downtime(r))
            .Column("Root Cause", r => r.RootCause)
            .Column("Corrective Action", r => r.CorrectiveAction)
            .Write(items);

        return File("breakdown-history-report", csv);
    }

    public async Task<ReportFile> ExportAnalysisAsync(BreakdownAnalysisReportQuery query, CancellationToken cancellationToken)
    {
        Validate(query);
        var (groups, _) = await _repository.GetGroupsAsync(query, page: null, cancellationToken);

        var csv = new CsvReportWriter<BreakdownGroupDto>()
            .Column(GroupHeader(query.GroupBy!), r => r.Name)
            .Column("Code", r => r.Code)
            .Column("Breakdowns", r => Int(r.BreakdownCount))
            .Column("Open", r => Int(r.OpenCount))
            .Column("Resolved", r => Int(r.ResolvedCount))
            .Column("Resolved with Downtime", r => Int(r.DowntimeRecordCount))
            .Column("Total Downtime (hrs)", r => Hours(r.TotalDowntimeHours))
            .Column("Average Downtime (hrs)", r => Hours(r.AverageDowntimeHours))
            .Column("Longest Downtime (hrs)", r => Hours(r.LongestDowntimeHours))
            .Write(groups.Select(BreakdownReportQueryBuilder.ToDto));

        return File($"breakdown-analysis-by-{query.GroupBy}-report", csv);
    }

    private static CsvReportWriter<MachineBreakdownReportItemDto> Identity(CsvReportWriter<MachineBreakdownReportItemDto> writer) => writer
        .Column("Breakdown No", r => r.BreakdownNo)
        .Column("Machine Code", r => r.MachineCode)
        .Column("Machine Name", r => r.MachineName)
        .Column("Department", r => r.DepartmentName);

    private static string GroupHeader(string groupBy) => groupBy switch
    {
        BreakdownReportGroupBy.Type => "Breakdown Type",
        BreakdownReportGroupBy.Department => "Department",
        BreakdownReportGroupBy.Priority => "Priority",
        BreakdownReportGroupBy.Stage => "Stage",
        BreakdownReportGroupBy.Status => "Status",
        _ => "Machine",
    };

    // ================================================================ Validation

    /// <summary>List / Downtime: every breakdown filter. History: status is fixed (Resolved), stage limited to Resolved / Closed.</summary>
    private static void Validate(MachineBreakdownReportQuery query, bool history)
    {
        var errors = new List<string>();
        CommonFilters(query, errors);
        query.SortBy = Canonical(query.SortBy, MachineReportBreakdownSort.All, "SortBy", errors);

        if (history)
        {
            if (Trimmed(query.Status) is not null && !string.Equals(query.Status!.Trim(), MachineReportBreakdownStatus.Resolved, StringComparison.OrdinalIgnoreCase))
                errors.Add("Status does not apply to the history report (it lists resolved breakdowns only).");
            query.Status = MachineReportBreakdownStatus.Resolved;
            query.Stage = Canonical(query.Stage, new[] { BreakdownStage.Resolved, BreakdownStage.Closed }, "Stage", errors);
        }
        else
        {
            query.Status = Canonical(query.Status, MachineReportBreakdownStatus.All, "Status", errors);
            query.Stage = Canonical(query.Stage, BreakdownStage.All, "Stage", errors);
        }

        ThrowIfAny(errors);
    }

    private static void Validate(BreakdownAnalysisReportQuery query)
    {
        var errors = new List<string>();
        CommonFilters(query, errors);
        query.Status = Canonical(query.Status, MachineReportBreakdownStatus.All, "Status", errors);
        query.Stage = Canonical(query.Stage, BreakdownStage.All, "Stage", errors);
        query.GroupBy = Canonical(query.GroupBy, BreakdownReportGroupBy.All, "GroupBy", errors) ?? BreakdownReportGroupBy.Machine;
        query.SortBy = Canonical(query.SortBy, BreakdownReportGroupSort.All, "SortBy", errors);
        ThrowIfAny(errors);
    }

    private static void CommonFilters(MachineBreakdownReportQuery query, List<string> errors)
    {
        query.Priority = Canonical(query.Priority, BreakdownPriority.All, "Priority", errors);
        query.SortDirection = Canonical(query.SortDirection, ReportSortDirection.All, "SortDirection", errors);
        query.ReportedBy = Trimmed(query.ReportedBy);
        CheckRange(query.FromDate, query.ToDate, "FromDate", "ToDate", errors);
    }

    // ================================================================ helpers

    private static string? Downtime(MachineBreakdownReportItemDto r) =>
        r.Status == MachineReportBreakdownStatus.Open ? "Ongoing" : Hours(r.DowntimeHours);

    private static string? Hours(decimal? value) => CsvReportWriter<object>.Number(value);
    private static string? Date(DateOnly? value) => CsvReportWriter<object>.Date(value);
    private static string? Time(TimeOnly? value) => CsvReportWriter<object>.Time(value);
    private static string? DateTime(DateTime? value) => CsvReportWriter<object>.DateTime(value);
    private static string Int(int value) => CsvReportWriter<object>.Number(value);

    private static ReportPage PageOf(PaginationRequest query) => new(query.PageNumber, query.PageSize);

    private ReportFile File(string name, byte[] content) =>
        new($"{name}-{_dateTimeProvider.Today:yyyyMMdd}.csv", CsvReportWriter<object>.ContentType, content);
}
