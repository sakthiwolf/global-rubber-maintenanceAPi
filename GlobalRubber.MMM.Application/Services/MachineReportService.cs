using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Domain.Constants;
using static GlobalRubber.MMM.Application.Common.ReportFilterValidation;

namespace GlobalRubber.MMM.Application.Services;

/// <summary>
/// Machine Reports - read-only. Validates and canonicalises the filters (fixed values are matched case-insensitively
/// and replaced by the stored spelling, so the SQL compares exact values), then delegates to IMachineReportRepository,
/// which filters, sorts, pages and aggregates in the database.
///
/// Nothing is recalculated here: last/next maintenance are the machine's system-managed dates, Overdue is the Machine
/// PM rule (not completed and due before today's plant date), downtime is the value the breakdown workflow stored at
/// Resolved (resolved - maintenance started). Open breakdowns have no downtime (user decision 2026-09-26).
/// </summary>
public sealed class MachineReportService : IMachineReportService
{
    private readonly IMachineReportRepository _repository;
    private readonly IDateTimeProvider _dateTimeProvider;

    public MachineReportService(IMachineReportRepository repository, IDateTimeProvider dateTimeProvider)
    {
        _repository = repository;
        _dateTimeProvider = dateTimeProvider;
    }

    // ================================================================ Screen (paged)

    public async Task<PagedResult<MachineListReportItemDto>> GetMachineListAsync(
        MachineListReportQuery query, CancellationToken cancellationToken)
    {
        Validate(query);
        var (items, total) = await _repository.GetMachineListAsync(query, PageOf(query), cancellationToken);
        return PagedResult<MachineListReportItemDto>.Create(items, query.PageNumber, query.PageSize, total);
    }

    public async Task<PagedResult<MachineMaintenanceHistoryReportItemDto>> GetMaintenanceHistoryAsync(
        MachineMaintenanceHistoryReportQuery query, CancellationToken cancellationToken)
    {
        Validate(query);
        var (items, total) = await _repository.GetMaintenanceHistoryAsync(
            query, _dateTimeProvider.Today, PageOf(query), cancellationToken); // plant (IST) date
        return PagedResult<MachineMaintenanceHistoryReportItemDto>.Create(items, query.PageNumber, query.PageSize, total);
    }

    public async Task<PagedResult<MachineBreakdownReportItemDto>> GetBreakdownsAsync(
        MachineBreakdownReportQuery query, CancellationToken cancellationToken)
    {
        Validate(query);
        var (items, total) = await _repository.GetBreakdownsAsync(query, PageOf(query), cancellationToken);
        return PagedResult<MachineBreakdownReportItemDto>.Create(items, query.PageNumber, query.PageSize, total);
    }

    public async Task<MachineDowntimeReportDto> GetDowntimeAsync(
        MachineBreakdownReportQuery query, CancellationToken cancellationToken)
    {
        Validate(query);
        var summary = await _repository.GetDowntimeSummaryAsync(query, cancellationToken);
        var (items, total) = await _repository.GetBreakdownsAsync(query, PageOf(query), cancellationToken);

        return new MachineDowntimeReportDto
        {
            Summary = summary,
            Page = PagedResult<MachineBreakdownReportItemDto>.Create(items, query.PageNumber, query.PageSize, total),
        };
    }

    public Task<MachineReportLookupsDto> GetLookupsAsync(CancellationToken cancellationToken) =>
        _repository.GetLookupsAsync(cancellationToken);

    // ================================================================ Export (whole filtered set, CSV)

    public async Task<ReportFile> ExportMachineListAsync(MachineListReportQuery query, CancellationToken cancellationToken)
    {
        Validate(query);
        var (items, _) = await _repository.GetMachineListAsync(query, page: null, cancellationToken);

        var csv = new CsvReportWriter<MachineListReportItemDto>()
            .Column("Machine Code", r => r.MachineCode)
            .Column("Machine Name", r => r.MachineName)
            .Column("Machine Type", r => r.MachineType)
            .Column("Department", r => r.DepartmentName)
            .Column("Location", r => r.Location)
            .Column("Criticality", r => r.Criticality)
            .Column("Operational Status", r => r.OperationalStatus)
            .Column("Active Status", r => r.IsActive ? "Active" : "Inactive")
            .Column("Last Maintenance", r => CsvReportWriter<MachineListReportItemDto>.Date(r.LastMaintenanceDate))
            .Column("Next Maintenance", r => CsvReportWriter<MachineListReportItemDto>.Date(r.NextMaintenanceDate))
            .Write(items);

        return File("machine-list-report", csv);
    }

    public async Task<ReportFile> ExportMaintenanceHistoryAsync(
        MachineMaintenanceHistoryReportQuery query, CancellationToken cancellationToken)
    {
        Validate(query);
        var (items, _) = await _repository.GetMaintenanceHistoryAsync(query, _dateTimeProvider.Today, page: null, cancellationToken);

        var csv = new CsvReportWriter<MachineMaintenanceHistoryReportItemDto>()
            .Column("PM No", r => r.PmNo)
            .Column("Machine Code", r => r.MachineCode)
            .Column("Machine Name", r => r.MachineName)
            .Column("Maintenance Plan", r => r.ChecklistCode is null ? null : $"{r.ChecklistCode} - {r.ChecklistName}")
            .Column("Frequency", r => r.Frequency)
            .Column("Maintenance Type", r => r.MaintenanceTypeName)
            .Column("Scheduled Date", r => CsvReportWriter<MachineMaintenanceHistoryReportItemDto>.Date(r.ScheduledDate))
            .Column("Completed Date", r => CsvReportWriter<MachineMaintenanceHistoryReportItemDto>.Date(r.CompletedDate))
            .Column("Status", r => r.Status)
            .Column("Performed By", r => r.MaintenanceBy)
            .Column("Checklist Items Done", r => CsvReportWriter<MachineMaintenanceHistoryReportItemDto>.Number(r.ChecklistItemsDone))
            .Column("Checklist Items Total", r => CsvReportWriter<MachineMaintenanceHistoryReportItemDto>.Number(r.ChecklistItemsTotal))
            .Column("Remarks", r => r.Remarks)
            .Write(items);

        return File("machine-maintenance-history-report", csv);
    }

    public async Task<ReportFile> ExportBreakdownsAsync(MachineBreakdownReportQuery query, CancellationToken cancellationToken)
    {
        Validate(query);
        var (items, _) = await _repository.GetBreakdownsAsync(query, page: null, cancellationToken);

        var csv = new CsvReportWriter<MachineBreakdownReportItemDto>()
            .Column("Breakdown No", r => r.BreakdownNo)
            .Column("Machine Code", r => r.MachineCode)
            .Column("Machine Name", r => r.MachineName)
            .Column("Breakdown Date", r => CsvReportWriter<MachineBreakdownReportItemDto>.Date(r.BreakdownDate))
            .Column("Breakdown Time", r => CsvReportWriter<MachineBreakdownReportItemDto>.Time(r.BreakdownTime))
            .Column("Problem", r => r.Problem)
            .Column("Breakdown Type", r => r.BreakdownTypeName)
            .Column("Priority", r => r.Priority)
            .Column("Stage", r => r.Stage)
            .Column("Status", r => r.Status)
            .Column("Reported By", r => r.ReportedBy)
            .Column("Assigned To", r => r.AssignedEngineerName)
            .Column("Assigned At", r => CsvReportWriter<MachineBreakdownReportItemDto>.DateTime(r.AssignedAt))
            .Column("Maintenance Started", r => CsvReportWriter<MachineBreakdownReportItemDto>.DateTime(r.MaintenanceStartedAt))
            .Column("Resolved At", r => CsvReportWriter<MachineBreakdownReportItemDto>.DateTime(r.ResolvedAt))
            .Column("Closed At", r => CsvReportWriter<MachineBreakdownReportItemDto>.DateTime(r.ClosedAt))
            .Column("Downtime (hrs)", r => CsvReportWriter<MachineBreakdownReportItemDto>.Number(r.DowntimeHours))
            .Column("Root Cause", r => r.RootCause)
            .Column("Corrective Action", r => r.CorrectiveAction)
            .Write(items);

        return File("machine-breakdown-report", csv);
    }

    public async Task<ReportFile> ExportDowntimeAsync(MachineBreakdownReportQuery query, CancellationToken cancellationToken)
    {
        Validate(query);
        var (items, _) = await _repository.GetBreakdownsAsync(query, page: null, cancellationToken);

        var csv = new CsvReportWriter<MachineBreakdownReportItemDto>()
            .Column("Breakdown No", r => r.BreakdownNo)
            .Column("Machine Code", r => r.MachineCode)
            .Column("Machine Name", r => r.MachineName)
            .Column("Breakdown Type", r => r.BreakdownTypeName)
            .Column("Problem", r => r.Problem)
            .Column("Priority", r => r.Priority)
            .Column("Breakdown Start", r => $"{CsvReportWriter<MachineBreakdownReportItemDto>.Date(r.BreakdownDate)} {CsvReportWriter<MachineBreakdownReportItemDto>.Time(r.BreakdownTime)}")
            .Column("Maintenance Started", r => CsvReportWriter<MachineBreakdownReportItemDto>.DateTime(r.MaintenanceStartedAt))
            .Column("Resolved At", r => CsvReportWriter<MachineBreakdownReportItemDto>.DateTime(r.ResolvedAt))
            .Column("Downtime (hrs)", r => r.Status == MachineReportBreakdownStatus.Open
                ? "Ongoing"
                : CsvReportWriter<MachineBreakdownReportItemDto>.Number(r.DowntimeHours))
            .Column("Status", r => r.Status == MachineReportBreakdownStatus.Open ? "Ongoing" : r.Status)
            .Write(items);

        return File("machine-downtime-report", csv);
    }

    // ================================================================ Validation

    private static void Validate(MachineListReportQuery query)
    {
        var errors = new List<string>();
        query.OperationalStatus = Canonical(query.OperationalStatus, MachineOperationalStatus.All, "OperationalStatus", errors);
        query.Criticality = Canonical(query.Criticality, MachineCriticality.All, "Criticality", errors);
        query.MachineType = Trimmed(query.MachineType);
        CheckRange(query.NextMaintenanceFrom, query.NextMaintenanceTo, "NextMaintenanceFrom", "NextMaintenanceTo", errors);
        ThrowIfAny(errors);
    }

    private static void Validate(MachineMaintenanceHistoryReportQuery query)
    {
        var errors = new List<string>();
        query.Status = Canonical(query.Status, MachineReportPmStatus.All, "Status", errors);
        CheckRange(query.FromDate, query.ToDate, "FromDate", "ToDate", errors);
        ThrowIfAny(errors);
    }

    private static void Validate(MachineBreakdownReportQuery query)
    {
        var errors = new List<string>();
        query.Priority = Canonical(query.Priority, BreakdownPriority.All, "Priority", errors);
        query.Stage = Canonical(query.Stage, BreakdownStage.All, "Stage", errors);
        query.Status = Canonical(query.Status, MachineReportBreakdownStatus.All, "Status", errors);
        query.SortBy = Canonical(query.SortBy, MachineReportBreakdownSort.All, "SortBy", errors);
        query.SortDirection = Canonical(query.SortDirection, ReportSortDirection.All, "SortDirection", errors);
        CheckRange(query.FromDate, query.ToDate, "FromDate", "ToDate", errors);
        ThrowIfAny(errors);
    }

    private static ReportPage PageOf(PaginationRequest query) => new(query.PageNumber, query.PageSize);

    private ReportFile File(string name, byte[] content) =>
        new($"{name}-{_dateTimeProvider.Today:yyyyMMdd}.csv", CsvReportWriter<object>.ContentType, content);
}
