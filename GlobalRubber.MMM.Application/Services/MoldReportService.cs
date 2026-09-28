using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Application.Services.Reports;
using GlobalRubber.MMM.Domain.Constants;
using GlobalRubber.MMM.Domain.Rules;
using static GlobalRubber.MMM.Application.Common.ReportFilterValidation;

namespace GlobalRubber.MMM.Application.Services;

/// <summary>
/// Mold Reports - read-only. Validates/canonicalises the filters, lets IMoldReportRepository filter, sort, page and
/// aggregate in the database, then applies the EXISTING rules to the page rows - nothing is recalculated differently:
///   life state          - masters.mold_master.life_state (persisted computed column: Replace / Warning / Normal)
///   life used %, remaining life - MoldService.LifeUsedPercent / max(0, maximum - current), as the Mold master shows them
///   PM threshold/remaining/state - MoldPmRules (NextThreshold, RemainingShots, StateOf), as the Mold PM page shows them
///   usage               - current_usage_shots is authoritative; range figures are Saved production entries (user decision
///                         2026-09-28 - historical usage at a date cannot be rebuilt: usage is also edited on the master)
///   replacement         - derived from status / life_state only; there is no replacement transaction (user decision
///                         2026-09-28), so nothing is ever reported as "replaced".
/// </summary>
public sealed class MoldReportService : IMoldReportService
{
    private readonly IMoldReportRepository _repository;
    private readonly IDateTimeProvider _dateTimeProvider;

    public MoldReportService(IMoldReportRepository repository, IDateTimeProvider dateTimeProvider)
    {
        _repository = repository;
        _dateTimeProvider = dateTimeProvider;
    }

    // ================================================================ Screen (paged)

    public async Task<PagedResult<MoldListReportItemDto>> GetMoldListAsync(MoldListReportQuery query, CancellationToken cancellationToken)
    {
        Validate(query);
        var (rows, total) = await _repository.GetMoldListAsync(query, PageOf(query), cancellationToken);
        var today = _dateTimeProvider.Today;
        return PagedResult<MoldListReportItemDto>.Create(rows.Select(r => ToListItem(r, today)).ToList(), query.PageNumber, query.PageSize, total);
    }

    public async Task<PagedResult<MoldUsageReportItemDto>> GetUsageAsync(MoldUsageReportQuery query, CancellationToken cancellationToken)
    {
        Validate(query);
        var (rows, total) = await _repository.GetUsageAsync(query, PageOf(query), cancellationToken);
        return PagedResult<MoldUsageReportItemDto>.Create(rows.Select(ToUsageItem).ToList(), query.PageNumber, query.PageSize, total);
    }

    public Task<MoldLifeReportDto> GetLifeStatusAsync(MoldLifeReportQuery query, CancellationToken cancellationToken) =>
        GetLifeAsync(query, replacementScope: false, cancellationToken);

    public Task<MoldLifeReportDto> GetReplacementAsync(MoldLifeReportQuery query, CancellationToken cancellationToken) =>
        GetLifeAsync(query, replacementScope: true, cancellationToken);

    public async Task<PagedResult<MoldMaintenanceReportItemDto>> GetMaintenanceAsync(
        MoldMaintenanceReportQuery query, CancellationToken cancellationToken)
    {
        Validate(query);
        var (items, total) = await _repository.GetMaintenanceAsync(query, _dateTimeProvider.Today, PageOf(query), cancellationToken);
        return PagedResult<MoldMaintenanceReportItemDto>.Create(items, query.PageNumber, query.PageSize, total);
    }

    public Task<MoldReportLookupsDto> GetLookupsAsync(CancellationToken cancellationToken) =>
        _repository.GetLookupsAsync(cancellationToken);

    private async Task<MoldLifeReportDto> GetLifeAsync(MoldLifeReportQuery query, bool replacementScope, CancellationToken cancellationToken)
    {
        Validate(query, replacementScope);
        var summary = await _repository.GetLifeSummaryAsync(query, replacementScope, cancellationToken);
        var (rows, total) = await _repository.GetLifeAsync(query, replacementScope, PageOf(query), cancellationToken);

        return new MoldLifeReportDto
        {
            Summary = summary,
            Page = PagedResult<MoldLifeReportItemDto>.Create(rows.Select(ToLifeItem).ToList(), query.PageNumber, query.PageSize, total),
        };
    }

    // ================================================================ Export (whole filtered set, CSV)

    public async Task<ReportFile> ExportMoldListAsync(MoldListReportQuery query, CancellationToken cancellationToken)
    {
        Validate(query);
        var (rows, _) = await _repository.GetMoldListAsync(query, page: null, cancellationToken);
        var today = _dateTimeProvider.Today;

        var csv = new CsvReportWriter<MoldListReportItemDto>()
            .Column("Mold Code", r => r.MoldCode)
            .Column("Mold Name", r => r.MoldName)
            .Column("Mold Type", r => r.MoldType)
            .Column("Cavities", r => Int(r.CavityCount))
            .Column("Product", r => $"{r.ProductCode} - {r.ProductName}")
            .Column("Location", r => r.Location)
            .Column("Current Usage (shots)", r => Int(r.CurrentUsageShots))
            .Column("Maximum Shots", r => Int(r.MaximumShots))
            .Column("Warning Shots", r => Int(r.WarningShots))
            .Column("Replacement Shots", r => Int(r.ReplacementShots))
            .Column("Life State", r => r.LifeState)
            .Column("Mold Status", r => r.Status)
            .Column("Active Status", r => r.IsActive ? "Active" : "Inactive (Retired)")
            .Column("Last Maintenance", r => CsvReportWriter<MoldListReportItemDto>.Date(r.LastMaintenanceDate))
            .Column("Usage at Last Maintenance", r => Int(r.LastMaintenanceUsage))
            .Column("PM Interval (shots)", r => Int(r.PmIntervalShots))
            .Column("Next PM Due At (shots)", r => Long(r.PmNextThresholdShots))
            .Column("Shots to Next PM", r => Long(r.PmRemainingShots))
            .Column("PM State", r => r.PmState)
            .Write(rows.Select(r => ToListItem(r, today)));

        return File("mold-list-report", csv);
    }

    public async Task<ReportFile> ExportUsageAsync(MoldUsageReportQuery query, CancellationToken cancellationToken)
    {
        Validate(query);
        var (rows, _) = await _repository.GetUsageAsync(query, page: null, cancellationToken);

        var csv = new CsvReportWriter<MoldUsageReportItemDto>()
            .Column("Mold Code", r => r.MoldCode)
            .Column("Mold Name", r => r.MoldName)
            .Column("Product", r => $"{r.ProductCode} - {r.ProductName}")
            .Column("Production Shots in Range", r => Long(r.RangeShots))
            .Column("Production Entries in Range", r => Int(r.RangeEntryCount))
            .Column("Last Production Date", r => CsvReportWriter<MoldUsageReportItemDto>.Date(r.LastProductionDate))
            .Column("Last Machine", r => r.LastMachineCode is null ? null : $"{r.LastMachineCode} - {r.LastMachineName}")
            .Column("Current Usage (shots)", r => Int(r.CurrentUsageShots))
            .Column("Maximum Shots", r => Int(r.MaximumShots))
            .Column("Replacement Shots", r => Int(r.ReplacementShots))
            .Column("Remaining Life (shots)", r => Int(r.RemainingShots))
            .Column("Life Used %", r => Int(r.LifeUsedPercent))
            .Column("Life State", r => r.LifeState)
            .Column("Mold Status", r => r.Status)
            .Write(rows.Select(ToUsageItem));

        return File("mold-usage-report", csv);
    }

    public Task<ReportFile> ExportLifeStatusAsync(MoldLifeReportQuery query, CancellationToken cancellationToken) =>
        ExportLifeAsync(query, replacementScope: false, "mold-life-status-report", cancellationToken);

    public Task<ReportFile> ExportReplacementAsync(MoldLifeReportQuery query, CancellationToken cancellationToken) =>
        ExportLifeAsync(query, replacementScope: true, "mold-replacement-report", cancellationToken);

    public async Task<ReportFile> ExportMaintenanceAsync(MoldMaintenanceReportQuery query, CancellationToken cancellationToken)
    {
        Validate(query);
        var (items, _) = await _repository.GetMaintenanceAsync(query, _dateTimeProvider.Today, page: null, cancellationToken);

        var csv = new CsvReportWriter<MoldMaintenanceReportItemDto>()
            .Column("PM No", r => r.PmNo)
            .Column("Mold Code", r => r.MoldCode)
            .Column("Mold Name", r => r.MoldName)
            .Column("Category", r => r.Category)
            .Column("Due Date", r => CsvReportWriter<MoldMaintenanceReportItemDto>.Date(r.ScheduledDate))
            .Column("Completed Date", r => CsvReportWriter<MoldMaintenanceReportItemDto>.Date(r.CompletedDate))
            .Column("Status", r => r.Status)
            .Column("Threshold (shots)", r => Int(r.ThresholdShots))
            .Column("Interval (shots)", r => Int(r.IntervalShots))
            .Column("Usage at Trigger", r => Int(r.UsageAtTrigger))
            .Column("Usage at Completion", r => Int(r.UsageAtCompletion))
            .Column("Maintenance By", r => r.MaintenanceBy)
            .Column("Remarks", r => r.Remarks)
            .Write(items);

        return File("mold-maintenance-report", csv);
    }

    private async Task<ReportFile> ExportLifeAsync(MoldLifeReportQuery query, bool replacementScope, string name, CancellationToken cancellationToken)
    {
        Validate(query, replacementScope);
        var (rows, _) = await _repository.GetLifeAsync(query, replacementScope, page: null, cancellationToken);

        var writer = new CsvReportWriter<MoldLifeReportItemDto>()
            .Column("Mold Code", r => r.MoldCode)
            .Column("Mold Name", r => r.MoldName)
            .Column("Mold Type", r => r.MoldType)
            .Column("Product", r => $"{r.ProductCode} - {r.ProductName}")
            .Column("Current Usage (shots)", r => Int(r.CurrentUsageShots))
            .Column("Maximum Shots", r => Int(r.MaximumShots))
            .Column("Warning Shots", r => Int(r.WarningShots))
            .Column("Replacement Shots", r => Int(r.ReplacementShots))
            .Column("Remaining Life (shots)", r => Int(r.RemainingShots))
            .Column("Life Used %", r => Int(r.LifeUsedPercent))
            .Column("Life State", r => r.LifeState)
            .Column("Mold Status", r => r.Status);

        if (replacementScope)
            writer.Column("Replacement Status", r => r.ReplacementStatus);

        return File(name, writer.Write(rows.Select(ToLifeItem)));
    }

    // ================================================================ Mapping (existing rules)

    private static MoldListReportItemDto ToListItem(MoldListRow r, DateOnly today)
    {
        var enabled = MoldPmRules.IsEnabled(r.IntervalShots);
        return new MoldListReportItemDto
        {
            MoldId = r.MoldId,
            MoldCode = r.MoldCode,
            MoldName = r.MoldName,
            MoldType = r.MoldType,
            CavityCount = r.CavityCount,
            ProductCode = r.ProductCode,
            ProductName = r.ProductName,
            Location = r.Location,
            CurrentUsageShots = r.CurrentUsageShots,
            MaximumShots = r.MaximumShots,
            WarningShots = r.WarningShots,
            ReplacementShots = r.ReplacementShots,
            LifeState = r.LifeState,
            Status = r.Status,
            IsActive = r.Status != MoldStatus.Retired,
            LastMaintenanceDate = r.LastPmDate,
            LastMaintenanceUsage = r.LastPmUsage,
            PmIntervalShots = r.IntervalShots,
            PmNextThresholdShots = enabled ? MoldPmRules.NextThreshold(r.CycleStartShots, r.IntervalShots!.Value) : null,
            PmRemainingShots = enabled ? MoldPmRules.RemainingShots(r.CurrentUsageShots, r.CycleStartShots, r.IntervalShots!.Value) : null,
            PmState = MoldPmRules.StateOf(r.IntervalShots, r.CycleStartShots, r.PmWarningShots, r.CurrentUsageShots,
                r.OpenPmStatus, r.OpenPmScheduledDate, today),
        };
    }

    private static MoldUsageReportItemDto ToUsageItem(MoldUsageRow r) => new()
    {
        MoldId = r.MoldId,
        MoldCode = r.MoldCode,
        MoldName = r.MoldName,
        ProductCode = r.ProductCode,
        ProductName = r.ProductName,
        CurrentUsageShots = r.CurrentUsageShots,
        MaximumShots = r.MaximumShots,
        ReplacementShots = r.ReplacementShots,
        RemainingShots = RemainingLife(r),
        LifeUsedPercent = MoldService.LifeUsedPercent(r.CurrentUsageShots, r.MaximumShots),
        LifeState = r.LifeState,
        Status = r.Status,
        RangeShots = r.RangeShots,
        RangeEntryCount = r.RangeEntryCount,
        LastProductionDate = r.LastProductionDate,
        LastMachineCode = r.LastMachineCode,
        LastMachineName = r.LastMachineName,
    };

    private static MoldLifeReportItemDto ToLifeItem(MoldBaseRow r) => new()
    {
        MoldId = r.MoldId,
        MoldCode = r.MoldCode,
        MoldName = r.MoldName,
        MoldType = r.MoldType,
        ProductCode = r.ProductCode,
        ProductName = r.ProductName,
        CurrentUsageShots = r.CurrentUsageShots,
        MaximumShots = r.MaximumShots,
        WarningShots = r.WarningShots,
        ReplacementShots = r.ReplacementShots,
        RemainingShots = RemainingLife(r),
        LifeUsedPercent = MoldService.LifeUsedPercent(r.CurrentUsageShots, r.MaximumShots),
        LifeState = r.LifeState,
        Status = r.Status,
        ReplacementStatus = r.Status == MoldStatus.Retired
            ? MoldReplacementStatus.Retired
            : r.LifeState == MoldLifeState.Replace || r.Status == MoldStatus.ReplacementDue
                ? MoldReplacementStatus.ReplacementRequired
                : null,
    };

    /// <summary>The Mold master's remaining life: max(0, maximum - current) (MoldService.MapToDto).</summary>
    private static int RemainingLife(MoldBaseRow r) => Math.Max(0, r.MaximumShots - r.CurrentUsageShots);

    // ================================================================ Validation

    private static void Validate(MoldListReportQuery query)
    {
        var errors = new List<string>();
        query.Status = Canonical(query.Status, MoldStatus.All, "Status", errors);
        query.LifeState = Canonical(query.LifeState, MoldLifeState.All, "LifeState", errors);
        query.MoldType = Trimmed(query.MoldType);
        ThrowIfAny(errors);
    }

    private static void Validate(MoldUsageReportQuery query)
    {
        var errors = new List<string>();
        query.LifeState = Canonical(query.LifeState, MoldLifeState.All, "LifeState", errors);
        query.SortBy = Canonical(query.SortBy, MoldReportSort.UsageSorts, "SortBy", errors);
        query.SortDirection = Canonical(query.SortDirection, ReportSortDirection.All, "SortDirection", errors);
        CheckRange(query.FromDate, query.ToDate, "FromDate", "ToDate", errors);
        ThrowIfAny(errors);
    }

    private static void Validate(MoldLifeReportQuery query, bool replacementScope)
    {
        var errors = new List<string>();
        query.Status = Canonical(query.Status, MoldStatus.All, "Status", errors);
        query.LifeState = Canonical(query.LifeState, MoldLifeState.All, "LifeState", errors);
        query.SortBy = Canonical(query.SortBy, MoldReportSort.LifeSorts, "SortBy", errors);
        query.SortDirection = Canonical(query.SortDirection, ReportSortDirection.All, "SortDirection", errors);

        if (replacementScope)
            query.ReplacementStatus = Canonical(query.ReplacementStatus, MoldReplacementStatus.All, "ReplacementStatus", errors);
        else if (Trimmed(query.ReplacementStatus) is not null)
            errors.Add("ReplacementStatus applies to the replacement report only.");

        ThrowIfAny(errors);
    }

    private static void Validate(MoldMaintenanceReportQuery query)
    {
        var errors = new List<string>();
        query.Category = Canonical(query.Category, MoldPmCategory.All, "Category", errors);
        query.Status = Canonical(query.Status, MoldPmBucket.All, "Status", errors);
        CheckRange(query.FromDate, query.ToDate, "FromDate", "ToDate", errors);
        ThrowIfAny(errors);
    }

    // ================================================================ helpers

    private static string? Int(int? value) => value?.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string? Long(long? value) => value?.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static ReportPage PageOf(PaginationRequest query) => new(query.PageNumber, query.PageSize);

    private ReportFile File(string name, byte[] content) =>
        new($"{name}-{_dateTimeProvider.Today:yyyyMMdd}.csv", CsvReportWriter<object>.ContentType, content);
}
