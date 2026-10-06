using System.Globalization;
using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Domain.Constants;
using GlobalRubber.MMM.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace GlobalRubber.MMM.Application.Services;

/// <summary>
/// Manual / One-Time preventive maintenance (migration 025, user decisions 2026-10-06): "schedule maintenance for this
/// specific machine or mold on this specific date". Exactly ONE PM record is created (schedule type Manual, a title, no plan
/// - checklist_id NULL - so completion can never create a successor); the selected Checklist Master's items are copied
/// into the PM's snapshot exactly as a plan occurrence copies them. Nothing else is written: no plan, no machine/mold master
/// column (a machine's next maintenance date counts automatic occurrences only; a mold's usage-based cycle is untouched).
///
/// Validation, reusing the plan rules: Title required (trimmed, at most 150); machine/mold required - unknown = 404, an
/// inactive machine / a Retired mold = 400; date required (any date - past dates are allowed, like a plan's start date, and
/// are then overdue); Maintenance Type optional (Machine only - mold PMs have none, Q-15): unknown = 404, must apply to
/// Machine or Both and be active (400); Checklist Master optional: unknown = 404, must be a master of the same Applies To,
/// active, with items (400). There is no duplicate rule for the same target and date (none exists for PMs).
///
/// Afterwards (never failing the saved PM): an audit entry (MachinePmManualScheduled / MoldPmManualScheduled) and, for a
/// machine, the existing "Preventive maintenance scheduled" notification; its Due / Overdue notifications come from the
/// existing scanner, which covers every open machine PM. Mold PMs have no date notifications.
/// </summary>
public sealed class ManualPmService : IManualPmService
{
    private const int TitleMaxLength = 150; // title NVARCHAR(150)

    private readonly IMachinePmRepository _machinePmRepository;
    private readonly IMoldPmRepository _moldPmRepository;
    private readonly IMachinePmService _machinePmService;
    private readonly IMoldPmService _moldPmService;
    private readonly IMachineRepository _machineRepository;
    private readonly IMoldRepository _moldRepository;
    private readonly IMaintenanceTypeRepository _maintenanceTypeRepository;
    private readonly IMaintenanceChecklistRepository _checklistRepository;
    private readonly IUserRepository _userRepository;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly IAuditLogService _auditLogService;
    private readonly INotificationPublisher _notificationPublisher;
    private readonly ILogger<ManualPmService> _logger;

    public ManualPmService(
        IMachinePmRepository machinePmRepository,
        IMoldPmRepository moldPmRepository,
        IMachinePmService machinePmService,
        IMoldPmService moldPmService,
        IMachineRepository machineRepository,
        IMoldRepository moldRepository,
        IMaintenanceTypeRepository maintenanceTypeRepository,
        IMaintenanceChecklistRepository checklistRepository,
        IUserRepository userRepository,
        IDateTimeProvider dateTimeProvider,
        IAuditLogService auditLogService,
        INotificationPublisher notificationPublisher,
        ILogger<ManualPmService> logger)
    {
        _machinePmRepository = machinePmRepository;
        _moldPmRepository = moldPmRepository;
        _machinePmService = machinePmService;
        _moldPmService = moldPmService;
        _machineRepository = machineRepository;
        _moldRepository = moldRepository;
        _maintenanceTypeRepository = maintenanceTypeRepository;
        _checklistRepository = checklistRepository;
        _userRepository = userRepository;
        _dateTimeProvider = dateTimeProvider;
        _auditLogService = auditLogService;
        _notificationPublisher = notificationPublisher;
        _logger = logger;
    }

    public async Task<MachinePmDto> ScheduleMachineAsync(
        ScheduleManualMachinePmRequest request, int? actingUserId, string? ipAddress, CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        var title = ValidateCommon(request.Title, request.MaintenanceDate, errors);
        if (request.MachineId is not > 0) errors.Add("MachineId is required.");
        if (request.MaintenanceTypeId is <= 0) errors.Add("MaintenanceTypeId is not valid.");
        if (request.SourceChecklistId is <= 0) errors.Add("SourceChecklistId is not valid.");
        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        var machine = await _machineRepository.GetByIdAsync(request.MachineId!.Value, cancellationToken)
            ?? throw new NotFoundException(nameof(Machine), request.MachineId.Value);
        if (!machine.IsActive)
        {
            throw new ValidationException("The selected machine is not active.");
        }

        var maintenanceType = await ResolveMaintenanceTypeAsync(request.MaintenanceTypeId, cancellationToken);
        var master = await ResolveChecklistMasterAsync(request.SourceChecklistId, MaintenanceChecklistAppliesTo.Machine, cancellationToken);

        var pm = new MachinePm
        {
            MachineId = machine.MachineId,
            ScheduleType = PmScheduleType.Manual,
            Title = title,
            ChecklistId = null, // no plan: never a successor
            ScheduledDate = request.MaintenanceDate!.Value,
            Status = MachinePmStatus.Scheduled, // the same initial status as an automatic occurrence
            MaintenanceTypeId = maintenanceType?.MaintenanceTypeId,
            CreatedAt = _dateTimeProvider.UtcNow,
            CreatedBy = actingUserId,
            ChecklistItems = master is null ? new() : MachinePmOccurrenceFactory.Snapshot(master.Items), // the existing snapshot copy
        };

        var created = await _machinePmRepository.AddManualAsync(pm, cancellationToken);

        var details = new List<AuditLogDetailEntry>
        {
            new("schedule_type", null, PmScheduleType.Manual),
            new("title", null, created.Title),
            new("machine_code", null, machine.MachineCode),
            new("scheduled_date", null, Date(created.ScheduledDate)),
        };
        if (maintenanceType is not null) details.Add(new AuditLogDetailEntry("maintenance_type", null, maintenanceType.MaintenanceTypeName));
        if (master is not null) details.Add(new AuditLogDetailEntry("checklist_master", null, master.ChecklistCode));

        await WriteAuditAsync(MachinePmAuditNames.Module, MachinePmAuditNames.ManualScheduled, MachinePmAuditNames.EntityName,
            created.MachinePmId, created.PmNo, actingUserId, ipAddress,
            actor => $"{actor} scheduled manual (one-time) preventive maintenance {created.PmNo} '{created.Title}' for machine " +
                     $"{machine.MachineCode} on {Date(created.ScheduledDate)} ({created.ChecklistItems.Count} checklist items).",
            details, cancellationToken);

        // The existing Machine PM notification (migration 021) - after the PM has committed, like the audit.
        await MachinePmNotifications.ScheduledAsync(
            _notificationPublisher, created.MachinePmId, created.PmNo, machine.MachineCode, created.ScheduledDate, actingUserId, cancellationToken);

        return await _machinePmService.GetByIdAsync(created.MachinePmId, cancellationToken);
    }

    public async Task<MoldPmDto> ScheduleMoldAsync(
        ScheduleManualMoldPmRequest request, int? actingUserId, string? ipAddress, CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        var title = ValidateCommon(request.Title, request.MaintenanceDate, errors);
        if (request.MoldId is not > 0) errors.Add("MoldId is required.");
        if (request.SourceChecklistId is <= 0) errors.Add("SourceChecklistId is not valid.");
        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        var mold = await _moldRepository.GetByIdAsync(request.MoldId!.Value, cancellationToken)
            ?? throw new NotFoundException(nameof(Mold), request.MoldId.Value);
        if (mold.Status == MoldStatus.Retired)
        {
            throw new ValidationException("The selected mold is retired."); // Retired is the mold's inactive state
        }

        var master = await ResolveChecklistMasterAsync(request.SourceChecklistId, MaintenanceChecklistAppliesTo.Mold, cancellationToken);

        var pm = new MoldPm
        {
            MoldId = mold.MoldId,
            ScheduleType = PmScheduleType.Manual,
            Title = title,
            Category = MoldPmCategory.Scheduled, // never 'Shot-based': not part of the usage-based cycle
            ChecklistId = null,
            ScheduledDate = request.MaintenanceDate!.Value,
            Status = MoldPmStatus.Scheduled,
            ThresholdShots = null,
            IntervalShots = null,
            UsageReset = false,
            CreatedAt = _dateTimeProvider.UtcNow,
            CreatedBy = actingUserId,
            ChecklistItems = master is null
                ? new()
                : master.Items
                    .OrderBy(i => i.SortOrder).ThenBy(i => i.ChecklistItemId)
                    .Select(i => new MoldPmChecklistItem { SortOrder = i.SortOrder, ItemLabel = i.ItemLabel, ChecklistItemId = i.ChecklistItemId, IsChecked = false })
                    .ToList(),
        };

        var created = await _moldPmRepository.AddManualAsync(pm, cancellationToken); // usage at creation read under the mold lock

        var details = new List<AuditLogDetailEntry>
        {
            new("schedule_type", null, PmScheduleType.Manual),
            new("title", null, created.Title),
            new("mold_code", null, mold.MoldCode),
            new("scheduled_date", null, Date(created.ScheduledDate)),
            new("usage_at_trigger", null, created.MoldUsageAtService.ToString(CultureInfo.InvariantCulture)),
        };
        if (master is not null) details.Add(new AuditLogDetailEntry("checklist_master", null, master.ChecklistCode));

        await WriteAuditAsync(MoldPmAuditNames.Module, MoldPmAuditNames.ManualScheduled, MoldPmAuditNames.EntityName,
            created.MoldPmId, created.PmNo, actingUserId, ipAddress,
            actor => $"{actor} scheduled manual (one-time) preventive maintenance {created.PmNo} '{created.Title}' for mold " +
                     $"{mold.MoldCode} on {Date(created.ScheduledDate)} ({created.ChecklistItems.Count} checklist items).",
            details, cancellationToken);

        return await _moldPmService.GetByIdAsync(created.MoldPmId, cancellationToken);
    }

    private static string? ValidateCommon(string? titleInput, DateOnly? maintenanceDate, List<string> errors)
    {
        var title = string.IsNullOrWhiteSpace(titleInput) ? null : titleInput.Trim();
        if (title is null) errors.Add("Title is required.");
        else if (title.Length > TitleMaxLength) errors.Add($"Title must be at most {TitleMaxLength} characters.");
        if (maintenanceDate is null) errors.Add("MaintenanceDate is required.");
        return title;
    }

    // The plan rule (MaintenanceChecklistService, migration 017): unknown -> 404; it must apply to Machine or Both and be active.
    private async Task<MaintenanceType?> ResolveMaintenanceTypeAsync(int? maintenanceTypeId, CancellationToken cancellationToken)
    {
        if (maintenanceTypeId is not { } id)
        {
            return null;
        }

        var maintenanceType = await _maintenanceTypeRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(MaintenanceType), id);
        if (maintenanceType.AppliesTo is not (MaintenanceTypeAppliesTo.Machine or MaintenanceTypeAppliesTo.Both))
        {
            throw new ValidationException("The selected maintenance type does not apply to machines.");
        }

        if (!maintenanceType.IsActive)
        {
            throw new ValidationException("The selected maintenance type is not active.");
        }

        return maintenanceType;
    }

    // The plan rule (MaintenanceChecklistService, migration 020): a Checklist Master of the same Applies To, active, with items.
    private async Task<MaintenanceChecklist?> ResolveChecklistMasterAsync(int? sourceId, string appliesTo, CancellationToken cancellationToken)
    {
        if (sourceId is not { } id)
        {
            return null;
        }

        var master = await _checklistRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException("Checklist Master", id);
        if (!master.IsChecklistMaster)
        {
            throw new ValidationException("The selected checklist is not a Checklist Master.");
        }

        if (!string.Equals(master.AppliesTo, appliesTo, StringComparison.Ordinal))
        {
            throw new ValidationException($"The selected Checklist Master does not apply to {appliesTo}.");
        }

        if (!master.IsActive)
        {
            throw new ValidationException("The selected Checklist Master is not active.");
        }

        if (master.Items.Count == 0)
        {
            throw new ValidationException("The selected Checklist Master has no checklist items.");
        }

        return master;
    }

    private static string Date(DateOnly value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    // Bookkeeping after the PM has committed - never turns that success into a failure (AuditLogService swallows its own
    // persistence errors and the user-name lookup is guarded).
    private async Task WriteAuditAsync(
        string module, string action, string entityName, int entityId, string recordRef, int? actingUserId, string? ipAddress,
        Func<string, string> describe, IReadOnlyList<AuditLogDetailEntry> details, CancellationToken cancellationToken)
    {
        var actingUserName = await AuditUserNameResolver.ResolveAsync(_userRepository, _logger, actingUserId, cancellationToken);

        await _auditLogService.LogAsync(new AuditLogEntry
        {
            UserId = actingUserId,
            UserName = actingUserName,
            Module = module,
            Action = action,
            EntityName = entityName,
            EntityId = entityId,
            RecordRef = recordRef,
            Description = describe(actingUserName),
            IpAddress = ipAddress,
            Details = details,
        }, cancellationToken);
    }
}
