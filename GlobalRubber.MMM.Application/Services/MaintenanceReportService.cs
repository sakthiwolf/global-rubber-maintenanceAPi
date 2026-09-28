using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Application.Services.Reports;
using GlobalRubber.MMM.Domain.Constants;
using static GlobalRubber.MMM.Application.Common.ReportFilterValidation;

namespace GlobalRubber.MMM.Application.Services;

/// <summary>
/// Maintenance Reports - read-only, over the two PM workflows (Machine PM and Mold PM; breakdowns and the deferred work
/// orders are not PM records - analysis 9.2 R-10..R-13). Statuses and "overdue" are each workflow's EXISTING rule (see
/// MaintenanceReportQueryBuilder); the page rows only get two presentation figures here:
///   DaysOverdue     - IST today - due date, for rows the rule marks Overdue (the rule's own date, nothing new)
///   RemainingShots  - open Mold PM with a threshold: threshold - current usage (MoldPmService.MapToDto's RemainingShots)
/// </summary>
public sealed class MaintenanceReportService : IMaintenanceReportService
{
    private readonly IMaintenanceReportRepository _repository;
    private readonly IDateTimeProvider _dateTimeProvider;

    public MaintenanceReportService(IMaintenanceReportRepository repository, IDateTimeProvider dateTimeProvider)
    {
        _repository = repository;
        _dateTimeProvider = dateTimeProvider;
    }

    public Task<PagedResult<MaintenanceReportItemDto>> GetScheduleAsync(MaintenanceReportQuery query, CancellationToken cancellationToken) =>
        GetPageAsync(query, MaintenanceReportScope.Schedule, cancellationToken);

    public Task<PagedResult<MaintenanceReportItemDto>> GetCompletedAsync(MaintenanceReportQuery query, CancellationToken cancellationToken) =>
        GetPageAsync(query, MaintenanceReportScope.Completed, cancellationToken);

    public Task<PagedResult<MaintenanceReportItemDto>> GetHistoryAsync(MaintenanceReportQuery query, CancellationToken cancellationToken) =>
        GetPageAsync(query, MaintenanceReportScope.History, cancellationToken);

    public async Task<MaintenanceOverdueReportDto> GetOverdueAsync(MaintenanceReportQuery query, CancellationToken cancellationToken)
    {
        Validate(query, MaintenanceReportScope.Overdue);
        var today = _dateTimeProvider.Today;
        var summary = await _repository.GetOverdueSummaryAsync(query, today, cancellationToken);
        var (rows, total) = await _repository.GetRowsAsync(query, MaintenanceReportScope.Overdue, today, PageOf(query), cancellationToken);

        return new MaintenanceOverdueReportDto
        {
            Summary = summary,
            Page = PagedResult<MaintenanceReportItemDto>.Create(rows.Select(r => ToDto(r, today)).ToList(), query.PageNumber, query.PageSize, total),
        };
    }

    public Task<MaintenanceReportLookupsDto> GetLookupsAsync(CancellationToken cancellationToken) =>
        _repository.GetLookupsAsync(cancellationToken);

    private async Task<PagedResult<MaintenanceReportItemDto>> GetPageAsync(
        MaintenanceReportQuery query, MaintenanceReportScope scope, CancellationToken cancellationToken)
    {
        Validate(query, scope);
        var today = _dateTimeProvider.Today;
        var (rows, total) = await _repository.GetRowsAsync(query, scope, today, PageOf(query), cancellationToken);
        return PagedResult<MaintenanceReportItemDto>.Create(rows.Select(r => ToDto(r, today)).ToList(), query.PageNumber, query.PageSize, total);
    }

    // ================================================================ Export (whole filtered set, current sort, CSV)

    public async Task<ReportFile> ExportScheduleAsync(MaintenanceReportQuery query, CancellationToken cancellationToken)
    {
        var rows = await AllAsync(query, MaintenanceReportScope.Schedule, cancellationToken);
        var csv = Identity(new CsvReportWriter<MaintenanceReportItemDto>())
            .Column("Due Date", r => D(r.ScheduledDate))
            .Column("Status", r => r.Status)
            .Column("Days Overdue", r => N(r.DaysOverdue))
            .Column("Current Usage (shots)", r => N(r.CurrentUsageShots))
            .Column("Threshold (shots)", r => N(r.ThresholdShots))
            .Column("Interval (shots)", r => N(r.IntervalShots))
            .Column("Remaining Shots", r => N(r.RemainingShots))
            .Column("Remarks", r => r.Remarks)
            .Write(rows);
        return File("pm-schedule-report", csv);
    }

    public async Task<ReportFile> ExportCompletedAsync(MaintenanceReportQuery query, CancellationToken cancellationToken)
    {
        var rows = await AllAsync(query, MaintenanceReportScope.Completed, cancellationToken);
        var csv = Identity(new CsvReportWriter<MaintenanceReportItemDto>())
            .Column("Due Date", r => D(r.ScheduledDate))
            .Column("Completed Date", r => D(r.CompletedDate))
            .Column("Completed By", r => r.PerformedBy)
            .Column("Status", r => r.Status)
            .Column("Usage at Trigger", r => N(r.UsageAtTrigger))
            .Column("Usage at Completion", r => N(r.UsageAtCompletion))
            .Column("Threshold (shots)", r => N(r.ThresholdShots))
            .Column("Interval (shots)", r => N(r.IntervalShots))
            .Column("Checklist Items Done", r => N(r.ChecklistItemsDone))
            .Column("Checklist Items Total", r => N(r.ChecklistItemsTotal))
            .Column("Remarks", r => r.Remarks)
            .Write(rows);
        return File("pm-completed-report", csv);
    }

    public async Task<ReportFile> ExportOverdueAsync(MaintenanceReportQuery query, CancellationToken cancellationToken)
    {
        var rows = await AllAsync(query, MaintenanceReportScope.Overdue, cancellationToken);
        var csv = Identity(new CsvReportWriter<MaintenanceReportItemDto>())
            .Column("Due Date", r => D(r.ScheduledDate))
            .Column("Days Overdue", r => N(r.DaysOverdue))
            .Column("Status", r => r.Status)
            .Column("Current Usage (shots)", r => N(r.CurrentUsageShots))
            .Column("Threshold (shots)", r => N(r.ThresholdShots))
            .Column("Interval (shots)", r => N(r.IntervalShots))
            .Column("Remaining Shots", r => N(r.RemainingShots))
            .Column("Remarks", r => r.Remarks)
            .Write(rows);
        return File("pm-overdue-report", csv);
    }

    public async Task<ReportFile> ExportHistoryAsync(MaintenanceReportQuery query, CancellationToken cancellationToken)
    {
        var rows = await AllAsync(query, MaintenanceReportScope.History, cancellationToken);
        var csv = Identity(new CsvReportWriter<MaintenanceReportItemDto>())
            .Column("Due Date", r => D(r.ScheduledDate))
            .Column("Completed Date", r => D(r.CompletedDate))
            .Column("Status", r => r.Status)
            .Column("Performed By", r => r.PerformedBy)
            .Column("Usage at Completion", r => N(r.UsageAtCompletion))
            .Column("Checklist Items Done", r => N(r.ChecklistItemsDone))
            .Column("Checklist Items Total", r => N(r.ChecklistItemsTotal))
            .Column("Remarks", r => r.Remarks)
            .Write(rows);
        return File("maintenance-history-report", csv);
    }

    private async Task<IEnumerable<MaintenanceReportItemDto>> AllAsync(
        MaintenanceReportQuery query, MaintenanceReportScope scope, CancellationToken cancellationToken)
    {
        Validate(query, scope);
        var today = _dateTimeProvider.Today;
        var (rows, _) = await _repository.GetRowsAsync(query, scope, today, page: null, cancellationToken);
        return rows.Select(r => ToDto(r, today));
    }

    /// <summary>The identifying columns every export starts with.</summary>
    private static CsvReportWriter<MaintenanceReportItemDto> Identity(CsvReportWriter<MaintenanceReportItemDto> writer) => writer
        .Column("PM No", r => r.PmNo)
        .Column("Source", r => r.Source)
        .Column("Asset Type", r => r.AssetType)
        .Column("Asset Code", r => r.AssetCode)
        .Column("Asset Name", r => r.AssetName)
        .Column("Category", r => r.Category)
        .Column("Maintenance Plan", r => r.PlanCode is null ? null : $"{r.PlanCode} - {r.PlanName}")
        .Column("Frequency", r => r.Frequency)
        .Column("Maintenance Type", r => r.MaintenanceTypeName);

    // ================================================================ Mapping

    private static MaintenanceReportItemDto ToDto(MaintenanceReportRow r, DateOnly today) => new()
    {
        Source = r.Source,
        AssetType = r.AssetType,
        RecordId = r.RecordId,
        PmNo = r.PmNo,
        AssetCode = r.AssetCode,
        AssetName = r.AssetName,
        Category = r.Category,
        PlanCode = r.PlanCode,
        PlanName = r.PlanName,
        Frequency = r.Frequency,
        MaintenanceTypeName = r.MaintenanceTypeName,
        ScheduledDate = r.ScheduledDate,
        CompletedDate = r.CompletedDate,
        Status = r.Status,
        IsOverdue = r.Status == MaintenanceReportStatus.Overdue,
        DaysOverdue = r.Status == MaintenanceReportStatus.Overdue ? today.DayNumber - r.ScheduledDate.DayNumber : null,
        PerformedBy = r.PerformedBy,
        CurrentUsageShots = r.CurrentUsageShots,
        ThresholdShots = r.ThresholdShots,
        IntervalShots = r.IntervalShots,
        UsageAtTrigger = r.UsageAtTrigger,
        UsageAtCompletion = r.UsageAtCompletion,
        RemainingShots = r.Status != MaintenanceReportStatus.Completed && r.ThresholdShots is { } t && r.CurrentUsageShots is { } u ? t - u : null,
        ChecklistItemsDone = r.ChecklistItemsDone,
        ChecklistItemsTotal = r.ChecklistItemsTotal,
        Remarks = r.Remarks,
    };

    // ================================================================ Validation

    private static void Validate(MaintenanceReportQuery query, MaintenanceReportScope scope)
    {
        var errors = new List<string>();
        query.AssetType = Canonical(query.AssetType, MaintenanceReportAssetType.All, "AssetType", errors);
        query.Category = Canonical(query.Category, MoldPmCategory.All, "Category", errors);
        query.SortBy = Canonical(query.SortBy, MaintenanceReportSort.All, "SortBy", errors);
        query.SortDirection = Canonical(query.SortDirection, ReportSortDirection.All, "SortDirection", errors);
        query.PerformedBy = Trimmed(query.PerformedBy);

        switch (scope)
        {
            case MaintenanceReportScope.Schedule:
                query.Status = Canonical(query.Status, MaintenanceReportStatus.Open, "Status", errors);
                break;
            case MaintenanceReportScope.History:
                query.Status = Canonical(query.Status, MaintenanceReportStatus.All, "Status", errors);
                break;
            default:
                if (Trimmed(query.Status) is not null)
                    errors.Add($"Status does not apply to the {(scope == MaintenanceReportScope.Completed ? "completed" : "overdue")} report.");
                query.Status = null;
                break;
        }

        CheckRange(query.FromDate, query.ToDate, "FromDate", "ToDate", errors);
        ThrowIfAny(errors);
    }

    // ================================================================ helpers

    private static string? D(DateOnly? value) => CsvReportWriter<MaintenanceReportItemDto>.Date(value);

    private static string? N(int? value) => value?.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static ReportPage PageOf(PaginationRequest query) => new(query.PageNumber, query.PageSize);

    private ReportFile File(string name, byte[] content) =>
        new($"{name}-{_dateTimeProvider.Today:yyyyMMdd}.csv", CsvReportWriter<object>.ContentType, content);
}
