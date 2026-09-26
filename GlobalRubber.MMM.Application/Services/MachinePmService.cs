using System.Globalization;
using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Domain.Common;
using GlobalRubber.MMM.Domain.Constants;
using GlobalRubber.MMM.Domain.Entities;
using GlobalRubber.MMM.Domain.Rules;
using Microsoft.Extensions.Logging;

namespace GlobalRubber.MMM.Application.Services;

/// <summary>
/// Machine Preventive Maintenance - recurring occurrences driven by the Maintenance Checklist (approved 2026-09-25).
/// Occurrences are created only by the checklist (first occurrence) and by completion (the successor); there is no manual
/// scheduling, start step, engineer assignment, edit, cancel or delete.
///
/// Completion (Function 5): Maintenance By is required (free text, at most 100, trimmed - not an employee); unchecked items
/// are allowed (Q-17); the PM becomes Completed on today's plant date keeping its due date, machine, checklist and number.
/// In the SAME transaction: if its checklist is still an ACTIVE MACHINE checklist with a frequency and a machine, exactly
/// ONE successor is created, due on RecurrenceRules.NextDueAfterCompletion(start date, frequency, due date, completion date)
/// (original cycle, missed dates skipped, never the same occurrence again), with a snapshot of the checklist's CURRENT
/// items; the machine's last maintenance date becomes the completion date and its next date the earliest open occurrence
/// (MachinePmRules); an operational status of Maintenance becomes Running (BR-23).
/// </summary>
public sealed class MachinePmService : IMachinePmService
{
    private const int RemarksMaxLength = 1000;        // remarks NVARCHAR(1000)
    private const int MaintenanceByMaxLength = 100;    // maintenance_by NVARCHAR(100)
    private const string CompletedAction = "MachinePmCompleted";

    private readonly IMachinePmRepository _repository;
    private readonly IUserRepository _userRepository;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<MachinePmService> _logger;

    public MachinePmService(
        IMachinePmRepository repository,
        IUserRepository userRepository,
        IDateTimeProvider dateTimeProvider,
        IAuditLogService auditLogService,
        ILogger<MachinePmService> logger)
    {
        _repository = repository;
        _userRepository = userRepository;
        _dateTimeProvider = dateTimeProvider;
        _auditLogService = auditLogService;
        _logger = logger;
    }

    public async Task<PagedResult<MachinePmDto>> GetAllAsync(MachinePmListQuery query, CancellationToken cancellationToken)
    {
        ValidateBucket(query);
        var (items, totalCount) = await _repository.GetAllAsync(query, _dateTimeProvider.Today, cancellationToken); // plant (IST) date

        return PagedResult<MachinePmDto>.Create(items.Select(MapToDto).ToList(), query.PageNumber, query.PageSize, totalCount);
    }

    public Task<MachinePmBucketCountsDto> GetBucketCountsAsync(MachinePmListQuery query, CancellationToken cancellationToken)
    {
        ValidateBucket(query);
        return _repository.GetBucketCountsAsync(query, _dateTimeProvider.Today, cancellationToken);
    }

    public async Task<MachinePmDto> GetByIdAsync(int machinePmId, CancellationToken cancellationToken)
    {
        var pm = await _repository.GetByIdAsync(machinePmId, cancellationToken)
            ?? throw new NotFoundException(nameof(MachinePm), machinePmId);

        return MapToDto(pm);
    }

    public Task<MachinePmLookupsDto> GetLookupsAsync(CancellationToken cancellationToken) =>
        _repository.GetLookupsAsync(cancellationToken);

    public async Task<MachinePmDto> CompleteAsync(
        int machinePmId, CompleteMachinePmRequest request, int? actingUserId, string? ipAddress, CancellationToken cancellationToken)
    {
        var pm = await _repository.GetByIdAsync(machinePmId, cancellationToken)
            ?? throw new NotFoundException(nameof(MachinePm), machinePmId);

        // Explicit rather than a silent no-op: nothing is written or audited.
        if (pm.Status == MachinePmStatus.Completed)
        {
            throw new ConflictException("The maintenance record is already completed.");
        }

        var maintenanceBy = string.IsNullOrWhiteSpace(request.MaintenanceBy) ? null : request.MaintenanceBy.Trim();
        var remarks = string.IsNullOrWhiteSpace(request.Remarks) ? null : request.Remarks.Trim();
        var results = request.Results ?? Array.Empty<MachinePmChecklistResultRequest>();
        var lineIds = pm.ChecklistItems.Select(i => i.MachinePmChecklistId).ToHashSet();

        var errors = new List<string>();
        if (maintenanceBy is null) errors.Add("MaintenanceBy is required.");
        else if (maintenanceBy.Length > MaintenanceByMaxLength) errors.Add($"MaintenanceBy must be at most {MaintenanceByMaxLength} characters.");
        if (string.IsNullOrWhiteSpace(request.RowVersion)) errors.Add("RowVersion is required.");
        else if (!TryDecodeRowVersion(request.RowVersion, out _)) errors.Add("RowVersion is not valid.");
        if (remarks is { Length: > RemarksMaxLength }) errors.Add($"Remarks must be at most {RemarksMaxLength} characters.");
        if (results.Any(r => !lineIds.Contains(r.MachinePmChecklistId))) errors.Add("A checklist result does not belong to this maintenance record.");
        if (results.GroupBy(r => r.MachinePmChecklistId).Any(g => g.Count() > 1)) errors.Add("A checklist item is listed more than once.");
        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        TryDecodeRowVersion(request.RowVersion, out var originalRowVersion);

        var today = _dateTimeProvider.Today; // plant date - never client-controlled
        var now = _dateTimeProvider.UtcNow;
        var ticks = results.ToDictionary(r => r.MachinePmChecklistId, r => r.IsChecked);
        foreach (var line in pm.ChecklistItems)
        {
            line.IsChecked = ticks.TryGetValue(line.MachinePmChecklistId, out var isChecked) && isChecked; // unlisted = unchecked
        }

        var statusBefore = pm.Status;
        var remarksBefore = pm.Remarks;
        pm.Status = MachinePmStatus.Completed;
        pm.CompletedDate = today;
        pm.MaintenanceBy = maintenanceBy;
        pm.Remarks = remarks;
        pm.UpdatedAt = now;
        pm.UpdatedBy = actingUserId;

        // Filled inside the transaction; read only after it has committed (and reset if the strategy retries).
        MachinePm? successor = null;
        var machineCodes = new Dictionary<int, string>();
        var machineChanges = new Dictionary<int, (DateOnly? LastBefore, DateOnly? LastAfter, DateOnly? NextBefore, DateOnly? NextAfter, string StatusBefore, string StatusAfter)>();

        var plan = new MachinePmCompletionPlan
        {
            // The successor goes on the checklist's machine; the PM's own machine is added by the repository.
            MachineIdsToLock = pm.Checklist?.MachineId is { } checklistMachineId ? new[] { checklistMachineId } : Array.Empty<int>(),
            BuildSuccessor = checklist =>
            {
                successor = IsSuccessorDue(checklist)
                    ? MachinePmOccurrenceFactory.NewOccurrence(
                        checklist!,
                        RecurrenceRules.NextDueAfterCompletion(checklist!.CycleAnchor(), checklist.Frequency!, pm.ScheduledDate, today),
                        now, actingUserId)
                    : null;
                return successor;
            },
            ApplyToLockedMachine = (machine, openDueDates) =>
            {
                var lastBefore = machine.LastMaintenanceDate;
                var nextBefore = machine.NextMaintenanceDate;
                var statusBeforeMachine = machine.OperationalStatus;
                if (machine.MachineId == pm.MachineId)
                {
                    machine.LastMaintenanceDate = today;
                    machine.OperationalStatus = MachinePmRules.OperationalStatusAfterCompletion(machine.OperationalStatus);
                }

                machine.NextMaintenanceDate = MachinePmRules.NextMaintenanceDateFromOpenOccurrences(openDueDates);
                if (lastBefore != machine.LastMaintenanceDate || nextBefore != machine.NextMaintenanceDate || statusBeforeMachine != machine.OperationalStatus)
                {
                    machine.UpdatedAt = now;
                    machine.UpdatedBy = actingUserId;
                }

                machineCodes[machine.MachineId] = machine.MachineCode;
                machineChanges[machine.MachineId] = (lastBefore, machine.LastMaintenanceDate, nextBefore, machine.NextMaintenanceDate, statusBeforeMachine, machine.OperationalStatus);
            },
        };

        var completed = await _repository.CompleteAsync(pm, originalRowVersion!, plan, cancellationToken);

        await WriteCompletionAuditAsync(completed, statusBefore, remarksBefore, successor, machineCodes, machineChanges, actingUserId, ipAddress, cancellationToken);
        if (successor is not null)
        {
            await WriteSuccessorAuditAsync(successor, completed, machineCodes, machineChanges, actingUserId, ipAddress, cancellationToken);
        }

        return MapToDto(completed);
    }

    // A successor is due only while the checklist is an ACTIVE MACHINE checklist with a complete configuration: a
    // deactivated checklist (Function 4 G) and a checklist changed to Mold (C2) never produce one.
    private static bool IsSuccessorDue(MaintenanceChecklist? checklist) =>
        checklist is { IsActive: true, AppliesTo: MaintenanceChecklistAppliesTo.Machine, Frequency: not null, MachineId: not null };

    private static void ValidateBucket(MachinePmListQuery query)
    {
        if (!string.IsNullOrWhiteSpace(query.Bucket) && !MachinePmBucket.All.Contains(query.Bucket))
        {
            throw new ValidationException($"Bucket must be one of: {string.Join(", ", MachinePmBucket.All)}.");
        }
    }

    private static string? Date(DateOnly? value) => value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static bool TryDecodeRowVersion(string value, out byte[]? bytes)
    {
        var buffer = new byte[value.Length];
        if (Convert.TryFromBase64String(value, buffer, out var written) && written > 0)
        {
            bytes = buffer[..written];
            return true;
        }

        bytes = null;
        return false;
    }

    // Everything after the business write has succeeded is audit bookkeeping and must never turn that success into a
    // failure - AuditLogService swallows its own persistence errors and the user-name lookup is guarded.
    private async Task WriteCompletionAuditAsync(
        MachinePm completed, string statusBefore, string? remarksBefore, MachinePm? successor,
        IReadOnlyDictionary<int, string> machineCodes,
        IReadOnlyDictionary<int, (DateOnly? LastBefore, DateOnly? LastAfter, DateOnly? NextBefore, DateOnly? NextAfter, string StatusBefore, string StatusAfter)> machineChanges,
        int? actingUserId, string? ipAddress, CancellationToken cancellationToken)
    {
        var details = new List<AuditLogDetailEntry>
        {
            new("status", statusBefore, completed.Status),
            new("completed_date", null, Date(completed.CompletedDate)),
            new("maintenance_by", null, completed.MaintenanceBy),
        };
        if (!string.Equals(remarksBefore, completed.Remarks, StringComparison.Ordinal))
        {
            details.Add(new AuditLogDetailEntry("remarks", remarksBefore, completed.Remarks));
        }

        if (successor is not null)
        {
            details.Add(new AuditLogDetailEntry("successor", null, $"{successor.PmNo} due {Date(successor.ScheduledDate)}"));
        }

        foreach (var (machineId, change) in machineChanges.OrderBy(m => m.Key))
        {
            var code = machineCodes[machineId];
            if (change.LastBefore != change.LastAfter) details.Add(new AuditLogDetailEntry($"machine_last_maintenance_date ({code})", Date(change.LastBefore), Date(change.LastAfter)));
            if (change.NextBefore != change.NextAfter) details.Add(new AuditLogDetailEntry($"machine_next_maintenance_date ({code})", Date(change.NextBefore), Date(change.NextAfter)));
            if (change.StatusBefore != change.StatusAfter) details.Add(new AuditLogDetailEntry($"machine_operational_status ({code})", change.StatusBefore, change.StatusAfter));
        }

        var checkedCount = completed.ChecklistItems.Count(i => i.IsChecked);
        var machineCode = machineCodes.GetValueOrDefault(completed.MachineId, completed.Machine?.MachineCode ?? string.Empty);
        await WriteAuditAsync(CompletedAction, completed, actingUserId, ipAddress,
            actor => $"{actor} completed machine PM {completed.PmNo} for machine {machineCode} (due {Date(completed.ScheduledDate)}) on {Date(completed.CompletedDate)}, " +
                     $"performed by {completed.MaintenanceBy} ({checkedCount} of {completed.ChecklistItems.Count} checklist items checked)." +
                     (successor is null ? " No next occurrence." : $" Next occurrence {successor.PmNo} due {Date(successor.ScheduledDate)}."),
            details, cancellationToken);
    }

    private async Task WriteSuccessorAuditAsync(
        MachinePm successor, MachinePm completed, IReadOnlyDictionary<int, string> machineCodes,
        IReadOnlyDictionary<int, (DateOnly? LastBefore, DateOnly? LastAfter, DateOnly? NextBefore, DateOnly? NextAfter, string StatusBefore, string StatusAfter)> machineChanges,
        int? actingUserId, string? ipAddress, CancellationToken cancellationToken)
    {
        var code = machineCodes.GetValueOrDefault(successor.MachineId, string.Empty);
        var details = machineChanges.TryGetValue(successor.MachineId, out var change) && change.NextBefore != change.NextAfter
            ? new[] { new AuditLogDetailEntry("machine_next_maintenance_date", Date(change.NextBefore), Date(change.NextAfter)) }
            : Array.Empty<AuditLogDetailEntry>();

        await WriteAuditAsync(MachinePmAuditNames.Scheduled, successor, actingUserId, ipAddress,
            actor => $"{actor} scheduled machine PM {successor.PmNo} for machine {code} on {Date(successor.ScheduledDate)} " +
                     $"as the next occurrence after {completed.PmNo} ({successor.ChecklistItems.Count} items).",
            details, cancellationToken);
    }

    private async Task WriteAuditAsync(
        string action, MachinePm pm, int? actingUserId, string? ipAddress,
        Func<string, string> describe, IReadOnlyList<AuditLogDetailEntry>? details, CancellationToken cancellationToken)
    {
        var actingUserName = await AuditUserNameResolver.ResolveAsync(_userRepository, _logger, actingUserId, cancellationToken);

        await _auditLogService.LogAsync(new AuditLogEntry
        {
            UserId = actingUserId,
            UserName = actingUserName,
            Module = MachinePmAuditNames.Module,
            Action = action,
            EntityName = MachinePmAuditNames.EntityName,
            EntityId = pm.MachinePmId,
            RecordRef = pm.PmNo,
            Description = describe(actingUserName),
            IpAddress = ipAddress,
            Details = details ?? Array.Empty<AuditLogDetailEntry>(),
        }, cancellationToken);
    }

    private MachinePmDto MapToDto(MachinePm pm) => new()
    {
        MachinePmId = pm.MachinePmId,
        PmNo = pm.PmNo,
        MachineId = pm.MachineId,
        MachineCode = pm.Machine?.MachineCode ?? string.Empty,
        MachineName = pm.Machine?.MachineName ?? string.Empty,
        ChecklistId = pm.ChecklistId,
        ChecklistCode = pm.Checklist?.ChecklistCode,
        ChecklistName = pm.Checklist?.ChecklistName,
        Frequency = pm.Checklist?.Frequency,
        MaintenanceTypeId = pm.MaintenanceTypeId,
        MaintenanceTypeName = pm.MaintenanceType?.MaintenanceTypeName,
        ScheduledDate = pm.ScheduledDate,
        CompletedDate = pm.CompletedDate,
        MaintenanceBy = pm.MaintenanceBy,
        Remarks = pm.Remarks,
        Status = pm.Status,
        IsOverdue = pm.Status != MachinePmStatus.Completed && pm.ScheduledDate < _dateTimeProvider.Today, // plant (IST) date
        ChecklistItems = pm.ChecklistItems
            .OrderBy(i => i.SortOrder).ThenBy(i => i.MachinePmChecklistId)
            .Select(i => new MachinePmChecklistItemDto { MachinePmChecklistId = i.MachinePmChecklistId, SortOrder = i.SortOrder, ItemLabel = i.ItemLabel, IsChecked = i.IsChecked })
            .ToList(),
        CreatedAt = pm.CreatedAt,
        CreatedBy = pm.CreatedBy,
        UpdatedAt = pm.UpdatedAt,
        UpdatedBy = pm.UpdatedBy,
        RowVersion = Convert.ToBase64String(pm.RowVersion),
    };
}
