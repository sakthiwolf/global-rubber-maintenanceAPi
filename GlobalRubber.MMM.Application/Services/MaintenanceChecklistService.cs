using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using System.Globalization;
using GlobalRubber.MMM.Domain.Common;
using GlobalRubber.MMM.Domain.Constants;
using GlobalRubber.MMM.Domain.Entities;
using GlobalRubber.MMM.Domain.Rules;
using Microsoft.Extensions.Logging;

namespace GlobalRubber.MMM.Application.Services;

/// <summary>
/// Maintenance Checklist master (header + items). Rules are exactly those the analysis (4.10, BR-34, the validation table)
/// and the database define: name required (NVARCHAR(150)); applies-to required, Machine or Mold
/// (CK_maintenance_checklist_master_applies_to); at least one non-blank item, blank rows dropped, each label at most
/// NVARCHAR(200); items replaced as a set on edit; name unique among ACTIVE checklists (the all-masters rule, applied as
/// for the other masters - Q-27 is still open).
///
/// Recurring-PM configuration (migration 012, approved 2026-09-25): an ACTIVE checklist needs a Frequency (Daily / Weekly /
/// Monthly / Yearly); an active Machine checklist needs the Machine it belongs to; a Mold checklist never has a machine.
/// A new checklist is always active, so Create always requires them; editing an INACTIVE legacy checklist may keep them
/// empty. A newly chosen machine must exist (404) and be active (400); an unchanged machine that has been deactivated
/// since stays assigned (the project's convention for references). An active Machine checklist also needs a Start Date
/// (migration 013): the cycle's permanent anchor (legacy rows without one fall back to CreatedAt as an IST date).
///
/// Creating an active MACHINE checklist also creates its first Machine PM occurrence in the SAME transaction (Function 3):
/// due on its Start Date, Scheduled, no maintenance type / engineer / maintenance
/// by, a snapshot of the checklist's items, a number from the MACHINE_PM sequence; the machine's next maintenance date
/// becomes the earliest of its open occurrences. A Mold checklist creates no occurrence (Mold PM is not revised yet).
///
/// Editing an ACTIVE checklist keeps its open occurrence in step, in the SAME transaction (Function 4): item changes
/// refresh only the open occurrence's snapshot (completed history is never rewritten); a frequency change re-dates it to
/// RecurrenceRules.FirstOnOrAfter(anchor, new frequency, today), and so does a Start Date change (new anchor); a machine
/// change moves it (same PM number, same due date unless the frequency or start date also changed); Machine -&gt; Mold leaves it exactly as it is - completable, never a successor (C2);
/// Mold -&gt; Machine reuses such a kept occurrence (moved to the selected machine, re-dated, snapshot refreshed) or, if
/// there is none, creates a first occurrence. Every affected machine's next date is recalculated. An inactive checklist's
/// edits never touch its open occurrence; deactivation leaves it open and completable.
/// </summary>
public sealed class MaintenanceChecklistService : IMaintenanceChecklistService
{
    private const string ChecklistModule = "Maintenance Checklist"; // audit "Module" value: the menu name

    // Audit actions are "Checklist*", not "MaintenanceChecklist*": audit.audit_log.action is VARCHAR(30) and
    // "MaintenanceChecklistDeactivated" (31) does not fit - the audit row would be lost. Module + EntityName identify the master.
    private const int NameMaxLength = 150;                           // checklist_name NVARCHAR(150)
    private const int ItemLabelMaxLength = 200;                      // item_label NVARCHAR(200)
    private const string AtLeastOneItemMessage = "Please add at least one checklist item."; // analysis 4.10 / validation table

    private readonly IMaintenanceChecklistRepository _repository;
    private readonly IMachineRepository _machineRepository;
    private readonly IUserRepository _userRepository;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<MaintenanceChecklistService> _logger;

    public MaintenanceChecklistService(
        IMaintenanceChecklistRepository repository,
        IMachineRepository machineRepository,
        IUserRepository userRepository,
        IDateTimeProvider dateTimeProvider,
        IAuditLogService auditLogService,
        ILogger<MaintenanceChecklistService> logger)
    {
        _repository = repository;
        _machineRepository = machineRepository;
        _userRepository = userRepository;
        _dateTimeProvider = dateTimeProvider;
        _auditLogService = auditLogService;
        _logger = logger;
    }

    public async Task<PagedResult<MaintenanceChecklistDto>> GetAllAsync(MaintenanceChecklistListQuery query, CancellationToken cancellationToken)
    {
        var (items, totalCount) = await _repository.GetAllAsync(query, cancellationToken);

        return PagedResult<MaintenanceChecklistDto>.Create(items.Select(MapToDto).ToList(), query.PageNumber, query.PageSize, totalCount);
    }

    public async Task<MaintenanceChecklistDto> GetByIdAsync(int checklistId, CancellationToken cancellationToken)
    {
        var checklist = await _repository.GetByIdAsync(checklistId, cancellationToken)
            ?? throw new NotFoundException(nameof(MaintenanceChecklist), checklistId);

        return MapToDto(checklist);
    }

    public Task<MaintenanceChecklistLookupsDto> GetLookupsAsync(CancellationToken cancellationToken) =>
        _repository.GetLookupsAsync(cancellationToken);

    public async Task<MaintenanceChecklistDto> CreateAsync(
        CreateMaintenanceChecklistRequest request, int? actingUserId, string? ipAddress, CancellationToken cancellationToken)
    {
        // A new checklist is always active, so its recurring configuration is always required.
        var (name, appliesTo, frequency, machineId, startDate, labels) = NormalizeAndValidate(request, isActive: true, rowVersion: null, requireRowVersion: false, out _);

        await EnsureNameIsFreeAsync(name, excludeId: null, cancellationToken);

        var machine = await ResolveMachineAsync(machineId, currentMachineId: null, cancellationToken);

        var now = _dateTimeProvider.UtcNow;
        var checklist = new MaintenanceChecklist
        {
            // The id / code are never client-controlled: the code is issued by the repository from the
            // MAINTENANCE_CHECKLIST document sequence inside the insert's transaction.
            ChecklistName = name,
            AppliesTo = appliesTo,
            Frequency = frequency,
            MachineId = machineId, // the navigation stays unset so the insert never touches the machine row
            StartDate = startDate,
            IsActive = true, // a new checklist is always active - see CreateMaintenanceChecklistRequest
            CreatedAt = now,
            CreatedBy = actingUserId,
            Items = BuildItems(labels, now, actingUserId),
        };

        // Filled in by the plan inside the transaction; read only after it has committed.
        MachinePm? firstOccurrence = null;
        DateOnly? machineNextBefore = null;
        DateOnly? machineNextAfter = null;

        var plan = appliesTo == MaintenanceChecklistAppliesTo.Machine
            ? new MachinePmOccurrencePlan
            {
                // The first occurrence is due ON the start date (even one in the past: it is then simply overdue).
                BuildOccurrence = saved => firstOccurrence = MachinePmOccurrenceFactory.NewOccurrence(saved, saved.StartDate!.Value, now, actingUserId),
                ApplyToLockedMachine = (lockedMachine, openDueDates) =>
                {
                    machineNextBefore = lockedMachine.NextMaintenanceDate;
                    machineNextAfter = MachinePmRules.NextMaintenanceDateFromOpenOccurrences(openDueDates);
                    lockedMachine.NextMaintenanceDate = machineNextAfter;
                    lockedMachine.UpdatedAt = now;
                    lockedMachine.UpdatedBy = actingUserId;
                },
            }
            : null;

        var created = await _repository.AddAsync(checklist, plan, cancellationToken);
        created.Machine = machine;

        await WriteAuditAsync(
            "ChecklistCreated", created, actingUserId, ipAddress,
            actor => $"{actor} created {created.Frequency?.ToLowerInvariant()} checklist '{created.ChecklistName}' ({created.ChecklistCode}) for " +
                     (machine is null ? created.AppliesTo : $"machine {machine.MachineCode}") +
                     (created.StartDate is { } start ? $" starting {start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}" : string.Empty) +
                     $" with {Pluralize(created.Items.Count)}.",
            details: null, cancellationToken);

        if (firstOccurrence is not null)
        {
            await WriteFirstOccurrenceAuditAsync(firstOccurrence, created, machine!, machineNextBefore, machineNextAfter, actingUserId, ipAddress, cancellationToken);
        }

        return MapToDto(created);
    }

    public async Task<MaintenanceChecklistDto> UpdateAsync(
        int checklistId, UpdateMaintenanceChecklistRequest request, int? actingUserId, string? ipAddress, CancellationToken cancellationToken)
    {
        // Loaded without tracking; the caller's row version (not the one just read) is what guards the save.
        var checklist = await _repository.GetByIdAsync(checklistId, cancellationToken)
            ?? throw new NotFoundException(nameof(MaintenanceChecklist), checklistId);

        // The active-checklist requirements follow the checklist's stored status: an inactive legacy checklist may keep an
        // incomplete configuration (CK_maintenance_checklist_master_active_config).
        var (name, appliesTo, frequency, machineId, startDate, labels) = NormalizeAndValidate(
            request, checklist.IsActive, request.RowVersion, requireRowVersion: true, out var originalRowVersion);

        await EnsureNameIsFreeAsync(name, checklistId, cancellationToken);

        var machine = await ResolveMachineAsync(machineId, checklist.MachineId, cancellationToken);

        var before = (AppliesTo: checklist.AppliesTo, Frequency: checklist.Frequency, MachineId: checklist.MachineId, StartDate: checklist.StartDate);

        var details = new List<AuditLogDetailEntry>();
        if (!string.Equals(checklist.ChecklistName, name, StringComparison.Ordinal))
        {
            details.Add(new AuditLogDetailEntry("checklist_name", checklist.ChecklistName, name));
        }

        if (!string.Equals(checklist.AppliesTo, appliesTo, StringComparison.Ordinal))
        {
            details.Add(new AuditLogDetailEntry("applies_to", checklist.AppliesTo, appliesTo));
        }

        if (!string.Equals(checklist.Frequency, frequency, StringComparison.Ordinal))
        {
            details.Add(new AuditLogDetailEntry("frequency", checklist.Frequency, frequency));
        }

        if (checklist.MachineId != machineId)
        {
            details.Add(new AuditLogDetailEntry("machine", checklist.Machine?.MachineCode, machine?.MachineCode));
        }

        if (checklist.StartDate != startDate)
        {
            details.Add(new AuditLogDetailEntry("start_date", DateText(checklist.StartDate), DateText(startDate)));
        }

        // Items are rewritten only if the list (labels and order) actually changed, so an edit that only renames the
        // checklist keeps the item ids PM history points at. The audit records the item count, not the labels: the
        // joined labels could exceed audit_log_detail's 1000-character value columns.
        var currentLabels = checklist.Items.OrderBy(i => i.SortOrder).ThenBy(i => i.ChecklistItemId).Select(i => i.ItemLabel).ToList();
        var itemsChanged = !currentLabels.SequenceEqual(labels, StringComparer.Ordinal);
        if (itemsChanged)
        {
            details.Add(new AuditLogDetailEntry("items", Pluralize(currentLabels.Count), Pluralize(labels.Count)));
        }

        var now = _dateTimeProvider.UtcNow;
        checklist.ChecklistName = name;
        checklist.AppliesTo = appliesTo;
        checklist.Frequency = frequency;
        checklist.MachineId = machineId;
        checklist.StartDate = startDate;
        checklist.Machine = null; // never attached by the update: only machine_id is written
        checklist.UpdatedAt = now;
        checklist.UpdatedBy = actingUserId;
        if (itemsChanged)
        {
            checklist.Items = BuildItems(labels, now, actingUserId);
        }

        var sync = new OccurrenceSyncResult();
        var plan = BuildOccurrenceSyncPlan(checklist, before, itemsChanged, now, actingUserId, sync);

        var updated = await _repository.UpdateAsync(checklist, originalRowVersion!, itemsChanged, plan, cancellationToken);
        updated.Machine = machine;

        // What happened to the open occurrence and the machines is part of the same audited change.
        details.AddRange(sync.Details());

        var checklistFieldCount = details.Count - sync.DetailCount;
        var changeSummary = checklistFieldCount > 0
            ? $"Changed: {string.Join(", ", details.Take(checklistFieldCount).Select(d => d.FieldName))}."
            : "No field values changed.";

        await WriteAuditAsync(
            "ChecklistUpdated", updated, actingUserId, ipAddress,
            actor => $"{actor} updated checklist '{updated.ChecklistName}' ({updated.ChecklistCode}). {changeSummary}" + sync.Summary(),
            details, cancellationToken);

        if (sync.NewOccurrence is not null)
        {
            var next = sync.MachineDates.GetValueOrDefault(sync.NewOccurrence.MachineId);
            await WriteFirstOccurrenceAuditAsync(sync.NewOccurrence, updated, machine!, next.Before, next.After, actingUserId, ipAddress, cancellationToken);
        }

        return MapToDto(updated);
    }

    public async Task<MaintenanceChecklistDto> DeactivateAsync(
        int checklistId, int? actingUserId, string? ipAddress, CancellationToken cancellationToken)
    {
        var checklist = await _repository.GetByIdAsync(checklistId, cancellationToken)
            ?? throw new NotFoundException(nameof(MaintenanceChecklist), checklistId);

        // Explicit rather than a silent no-op (same as the other masters): nothing is written or audited.
        if (!checklist.IsActive)
        {
            throw new ConflictException("The checklist is already inactive.");
        }

        checklist.IsActive = false;
        checklist.UpdatedAt = _dateTimeProvider.UtcNow;
        checklist.UpdatedBy = actingUserId;

        var deactivated = await _repository.DeactivateAsync(checklist, cancellationToken);

        await WriteAuditAsync(
            "ChecklistDeactivated", deactivated, actingUserId, ipAddress,
            actor => $"{actor} deactivated checklist '{deactivated.ChecklistName}' ({deactivated.ChecklistCode}).",
            new[] { new AuditLogDetailEntry("is_active", "True", "False") }, cancellationToken);

        return MapToDto(deactivated);
    }

    // The first occurrence of an EXISTING checklist that becomes a Machine checklist (Mold -> Machine): the first cycle date
    // of its start date on or after today (plant date) - never a backlog of past cycle dates.
    private MachinePm BuildFirstOccurrence(MaintenanceChecklist saved, DateTime now, int? actingUserId)
    {
        var dueDate = RecurrenceRules.FirstOnOrAfter(saved.CycleAnchor(), saved.Frequency!, _dateTimeProvider.Today);

        return MachinePmOccurrenceFactory.NewOccurrence(saved, dueDate, now, actingUserId);
    }

    private static List<MachinePmChecklistItem> Snapshot(MaintenanceChecklist saved) => MachinePmOccurrenceFactory.Snapshot(saved);

    // Function 4: what an edit of an ACTIVE checklist does to its open occurrence and the machines involved. Null when
    // nothing about the occurrence can change (an inactive checklist, a Mold-only edit, or only the name changed).
    private ChecklistOccurrenceSyncPlan? BuildOccurrenceSyncPlan(
        MaintenanceChecklist saved, (string AppliesTo, string? Frequency, int? MachineId, DateOnly? StartDate) before, bool itemsChanged,
        DateTime now, int? actingUserId, OccurrenceSyncResult result)
    {
        var appliesToChanged = before.AppliesTo != saved.AppliesTo;
        var frequencyChanged = before.Frequency != saved.Frequency;
        var startDateChanged = before.StartDate != saved.StartDate; // a new anchor re-dates like a new frequency
        var machineChanged = before.MachineId != saved.MachineId;
        var touchesMachinePm = before.AppliesTo == MaintenanceChecklistAppliesTo.Machine || saved.AppliesTo == MaintenanceChecklistAppliesTo.Machine;

        if (!saved.IsActive || !touchesMachinePm || !(itemsChanged || frequencyChanged || startDateChanged || machineChanged || appliesToChanged))
        {
            return null;
        }

        var toLock = new[] { before.MachineId, saved.MachineId }.OfType<int>().Distinct().ToList();

        return new ChecklistOccurrenceSyncPlan
        {
            MachineIdsToLock = toLock,
            Decide = (checklist, open) =>
            {
                // Machine -> Mold (C2): the open occurrence stays exactly as it is - completable, never a successor.
                if (checklist.AppliesTo == MaintenanceChecklistAppliesTo.Mold)
                {
                    return OpenOccurrenceChange.None;
                }

                var anchor = checklist.CycleAnchor();

                if (open is null)
                {
                    // Mold -> Machine with no occurrence: a genuinely new first occurrence. Any other edit never creates one.
                    if (before.AppliesTo != MaintenanceChecklistAppliesTo.Mold)
                    {
                        return OpenOccurrenceChange.None;
                    }

                    result.NewOccurrence = BuildFirstOccurrence(checklist, now, actingUserId);
                    return new OpenOccurrenceChange { NewOccurrence = result.NewOccurrence };
                }

                result.Open = (open.PmNo, open.ScheduledDate, open.MachineId, open.ChecklistItems.Count);
                var reused = before.AppliesTo == MaintenanceChecklistAppliesTo.Mold; // kept by an earlier Machine -> Mold
                List<MachinePmChecklistItem>? snapshot = null;

                if (reused || frequencyChanged || startDateChanged)
                {
                    open.ScheduledDate = RecurrenceRules.FirstOnOrAfter(anchor, checklist.Frequency!, _dateTimeProvider.Today);
                }

                if (reused || machineChanged)
                {
                    open.MachineId = checklist.MachineId!.Value; // same PM number; due date kept unless re-dated above
                }

                if (reused || itemsChanged)
                {
                    snapshot = Snapshot(checklist);
                    result.Refreshed = true;
                }

                if (open.ScheduledDate != result.Open.Value.Date || open.MachineId != result.Open.Value.MachineId || snapshot is not null)
                {
                    open.UpdatedAt = now;
                    open.UpdatedBy = actingUserId;
                }

                result.OpenAfter = (open.ScheduledDate, open.MachineId, snapshot?.Count ?? open.ChecklistItems.Count);
                return new OpenOccurrenceChange { ReplacementSnapshot = snapshot };
            },
            ApplyToLockedMachine = (machine, openDueDates) =>
            {
                var nextBefore = machine.NextMaintenanceDate;
                var nextAfter = MachinePmRules.NextMaintenanceDateFromOpenOccurrences(openDueDates);
                result.MachineCodes[machine.MachineId] = machine.MachineCode;
                result.MachineDates[machine.MachineId] = (nextBefore, nextAfter);
                if (nextBefore != nextAfter)
                {
                    machine.NextMaintenanceDate = nextAfter;
                    machine.UpdatedAt = now;
                    machine.UpdatedBy = actingUserId;
                }
            },
        };
    }

    /// <summary>What the occurrence sync did, captured inside the transaction and audited after it has committed.</summary>
    private sealed class OccurrenceSyncResult
    {
        public (string PmNo, DateOnly Date, int MachineId, int Lines)? Open { get; set; }
        public (DateOnly Date, int MachineId, int Lines)? OpenAfter { get; set; }
        public MachinePm? NewOccurrence { get; set; }
        public Dictionary<int, string> MachineCodes { get; } = new();
        public Dictionary<int, (DateOnly? Before, DateOnly? After)> MachineDates { get; } = new();
        public int DetailCount { get; private set; }

        private static string? Date(DateOnly? d) => d?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        private string Code(int id) => MachineCodes.GetValueOrDefault(id, id.ToString(CultureInfo.InvariantCulture));

        public List<AuditLogDetailEntry> Details()
        {
            var details = new List<AuditLogDetailEntry>();
            if (Open is { } o && OpenAfter is { } a)
            {
                if (o.Date != a.Date) details.Add(new AuditLogDetailEntry($"open_pm_scheduled_date ({o.PmNo})", Date(o.Date), Date(a.Date)));
                if (o.MachineId != a.MachineId) details.Add(new AuditLogDetailEntry($"open_pm_machine ({o.PmNo})", Code(o.MachineId), Code(a.MachineId)));
                if (o.Lines != a.Lines || Refreshed) details.Add(new AuditLogDetailEntry($"open_pm_snapshot ({o.PmNo})", Items(o.Lines), Items(a.Lines)));
            }

            foreach (var (machineId, dates) in MachineDates.OrderBy(m => m.Key))
            {
                if (dates.Before != dates.After)
                {
                    details.Add(new AuditLogDetailEntry($"machine_next_maintenance_date ({Code(machineId)})", Date(dates.Before), Date(dates.After)));
                }
            }

            DetailCount = details.Count;
            return details;
        }

        public bool Refreshed { get; set; }

        public string Summary()
        {
            var parts = new List<string>();
            if (Open is { } o && OpenAfter is { } a)
            {
                if (o.MachineId != a.MachineId) parts.Add($"open PM {o.PmNo} moved from {Code(o.MachineId)} to {Code(a.MachineId)}");
                if (o.Date != a.Date) parts.Add($"open PM {o.PmNo} re-dated from {Date(o.Date)} to {Date(a.Date)}");
                if (Refreshed) parts.Add($"open PM {o.PmNo} checklist refreshed");
            }

            if (NewOccurrence is not null) parts.Add($"first occurrence {NewOccurrence.PmNo} scheduled for {Date(NewOccurrence.ScheduledDate)}");
            return parts.Count == 0 ? string.Empty : " " + string.Join("; ", parts) + ".";
        }

        private static string Items(int count) => count == 1 ? "1 item" : $"{count} items";
    }

    // The Machine PM module's audit convention (MachinePmScheduled, module "Machine Preventive Maintenance"), written
    // only after the transaction has committed; a failed audit write is swallowed like every other.
    private async Task WriteFirstOccurrenceAuditAsync(
        MachinePm occurrence, MaintenanceChecklist checklist, Machine machine, DateOnly? machineNextBefore, DateOnly? machineNextAfter,
        int? actingUserId, string? ipAddress, CancellationToken cancellationToken)
    {
        static string? Date(DateOnly? d) => d?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var actingUserName = await AuditUserNameResolver.ResolveAsync(_userRepository, _logger, actingUserId, cancellationToken);
        var details = machineNextBefore == machineNextAfter
            ? Array.Empty<AuditLogDetailEntry>()
            : new[] { new AuditLogDetailEntry("machine_next_maintenance_date", Date(machineNextBefore), Date(machineNextAfter)) };

        await _auditLogService.LogAsync(new AuditLogEntry
        {
            UserId = actingUserId,
            UserName = actingUserName,
            Module = MachinePmAuditNames.Module,
            Action = MachinePmAuditNames.Scheduled,
            EntityName = MachinePmAuditNames.EntityName,
            EntityId = occurrence.MachinePmId,
            RecordRef = occurrence.PmNo,
            Description = $"{actingUserName} scheduled machine PM {occurrence.PmNo} for machine {machine.MachineCode} on {Date(occurrence.ScheduledDate)} " +
                          $"from {checklist.Frequency!.ToLowerInvariant()} checklist {checklist.ChecklistCode} ({occurrence.ChecklistItems.Count} items).",
            IpAddress = ipAddress,
            Details = details,
        }, cancellationToken);
    }

    // Unknown -> 404; a NEWLY chosen machine must be active (400). An unchanged machine that has been deactivated since
    // stays assigned - the project's convention for references (approved C7: machine status does not stop the schedule).
    private async Task<Machine?> ResolveMachineAsync(int? machineId, int? currentMachineId, CancellationToken cancellationToken)
    {
        if (machineId is not { } id)
        {
            return null;
        }

        var machine = await _machineRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(Machine), id);

        if (!machine.IsActive && id != currentMachineId)
        {
            throw new ValidationException("The selected machine is not active.");
        }

        return machine;
    }

    private async Task EnsureNameIsFreeAsync(string name, int? excludeId, CancellationToken cancellationToken)
    {
        if (await _repository.ExistsActiveByNameAsync(name, excludeId, cancellationToken))
        {
            throw new ConflictException($"A checklist named '{name}' already exists.");
        }
    }

    // Trim everything; applies-to and frequency are matched case-insensitively onto the exact CK values; blank item rows
    // are dropped. One place so Create and Update can never disagree about what a valid checklist looks like; the row
    // version is only checked for Update. isActive = the checklist status the configuration must satisfy.
    private static (string Name, string AppliesTo, string? Frequency, int? MachineId, DateOnly? StartDate, IReadOnlyList<string> Labels) NormalizeAndValidate(
        CreateMaintenanceChecklistRequest request, bool isActive, string? rowVersion, bool requireRowVersion, out byte[]? originalRowVersion)
    {
        var name = (request.ChecklistName ?? string.Empty).Trim();
        var appliesToInput = string.IsNullOrWhiteSpace(request.AppliesTo) ? null : request.AppliesTo.Trim();
        var appliesTo = MaintenanceChecklistAppliesTo.All.FirstOrDefault(a => string.Equals(a, appliesToInput, StringComparison.OrdinalIgnoreCase));
        var frequencyInput = string.IsNullOrWhiteSpace(request.Frequency) ? null : request.Frequency.Trim();
        var frequency = ChecklistFrequency.All.FirstOrDefault(f => string.Equals(f, frequencyInput, StringComparison.OrdinalIgnoreCase));
        var labels = (request.Items ?? Array.Empty<MaintenanceChecklistItemRequest>())
            .Select(i => (i?.ItemLabel ?? string.Empty).Trim())
            .Where(l => l.Length > 0)
            .ToList();

        var errors = new List<string>();

        if (name.Length == 0) errors.Add("ChecklistName is required.");
        else if (name.Length > NameMaxLength) errors.Add($"ChecklistName must be at most {NameMaxLength} characters.");

        if (appliesToInput is null) errors.Add("AppliesTo is required.");
        else if (appliesTo is null) errors.Add($"AppliesTo must be one of: {string.Join(", ", MaintenanceChecklistAppliesTo.All)}.");

        // CK_maintenance_checklist_master_frequency / _active_config.
        if (frequencyInput is null)
        {
            if (isActive) errors.Add("Frequency is required.");
        }
        else if (frequency is null)
        {
            errors.Add($"Frequency must be one of: {string.Join(", ", ChecklistFrequency.All)}.");
        }

        // CK_maintenance_checklist_master_machine / _active_config.
        if (request.MachineId is <= 0)
        {
            errors.Add("MachineId is not valid.");
        }
        else if (appliesTo == MaintenanceChecklistAppliesTo.Mold && request.MachineId is not null)
        {
            errors.Add("A Mold checklist cannot have a machine.");
        }
        else if (appliesTo == MaintenanceChecklistAppliesTo.Machine && request.MachineId is null && isActive)
        {
            errors.Add("MachineId is required for a Machine checklist.");
        }

        // CK_maintenance_checklist_master_start_date: the anchor of an active Machine checklist's cycle.
        if (appliesTo == MaintenanceChecklistAppliesTo.Machine && request.StartDate is null && isActive)
        {
            errors.Add("StartDate is required for a Machine checklist.");
        }

        if (labels.Count == 0)
        {
            errors.Add(AtLeastOneItemMessage);
        }
        else
        {
            for (var i = 0; i < labels.Count; i++)
            {
                if (labels[i].Length > ItemLabelMaxLength)
                {
                    errors.Add($"Checklist item {i + 1} must be at most {ItemLabelMaxLength} characters.");
                }
            }
        }

        originalRowVersion = null;
        if (requireRowVersion)
        {
            if (string.IsNullOrWhiteSpace(rowVersion)) errors.Add("RowVersion is required.");
            else if (!TryDecodeRowVersion(rowVersion, out originalRowVersion)) errors.Add("RowVersion is not valid.");
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        return (name, appliesTo!, frequency, request.MachineId, request.StartDate, labels);
    }

    // sort_order is the 1-based position among the non-blank rows, so it is always consistent with what the user saw.
    private static List<MaintenanceChecklistItem> BuildItems(IReadOnlyList<string> labels, DateTime now, int? actingUserId) =>
        labels.Select((label, index) => new MaintenanceChecklistItem
        {
            SortOrder = index + 1,
            ItemLabel = label,
            CreatedAt = now,
            CreatedBy = actingUserId,
        }).ToList();

    private static string Pluralize(int count) => count == 1 ? "1 item" : $"{count} items";

    private static string? DateText(DateOnly? value) => value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

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
    private async Task WriteAuditAsync(
        string action, MaintenanceChecklist checklist, int? actingUserId, string? ipAddress,
        Func<string, string> describe, IReadOnlyList<AuditLogDetailEntry>? details, CancellationToken cancellationToken)
    {
        var actingUserName = await AuditUserNameResolver.ResolveAsync(_userRepository, _logger, actingUserId, cancellationToken);

        await _auditLogService.LogAsync(new AuditLogEntry
        {
            UserId = actingUserId,
            UserName = actingUserName,
            Module = ChecklistModule,
            Action = action,
            EntityName = "MaintenanceChecklist",
            EntityId = checklist.ChecklistId,
            RecordRef = checklist.ChecklistCode,
            Description = describe(actingUserName),
            IpAddress = ipAddress,
            Details = details ?? Array.Empty<AuditLogDetailEntry>(),
        }, cancellationToken);
    }

    private static MaintenanceChecklistDto MapToDto(MaintenanceChecklist checklist) => new()
    {
        ChecklistId = checklist.ChecklistId,
        ChecklistCode = checklist.ChecklistCode,
        ChecklistName = checklist.ChecklistName,
        AppliesTo = checklist.AppliesTo,
        Frequency = checklist.Frequency,
        MachineId = checklist.MachineId,
        MachineCode = checklist.Machine?.MachineCode,
        MachineName = checklist.Machine?.MachineName,
        MachineIsActive = checklist.Machine?.IsActive,
        StartDate = checklist.StartDate,
        IsActive = checklist.IsActive,
        Items = checklist.Items
            .OrderBy(i => i.SortOrder).ThenBy(i => i.ChecklistItemId)
            .Select(i => new MaintenanceChecklistItemDto { ChecklistItemId = i.ChecklistItemId, SortOrder = i.SortOrder, ItemLabel = i.ItemLabel })
            .ToList(),
        CreatedAt = checklist.CreatedAt,
        CreatedBy = checklist.CreatedBy,
        UpdatedAt = checklist.UpdatedAt,
        UpdatedBy = checklist.UpdatedBy,
        RowVersion = Convert.ToBase64String(checklist.RowVersion),
    };
}
