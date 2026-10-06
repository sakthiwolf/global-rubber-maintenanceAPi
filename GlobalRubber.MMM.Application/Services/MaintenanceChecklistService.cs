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
///
/// Checklist Masters (migration 020, approved 2026-10-05): the same table also holds reusable CHECKLIST MASTERS - name,
/// applies-to and items, no plan configuration (no frequency). A Machine plan selects one (SourceChecklistId) instead of
/// entering items: it has no items of its own and its PM occurrences copy the master's CURRENT items. The selected master
/// must exist, be a checklist master, apply to Machine, have at least one item and - when newly chosen - be active.
/// Plans created before migration 020 keep their own items until a master is chosen (no automatic conversion); Mold plans
/// never use a master. Choosing another master refreshes the open occurrence's snapshot like an item change; editing a
/// master's items affects only occurrences generated afterwards. A plan always keeps its frequency (a row without one IS a
/// checklist master), so plan and master can never be confused.
/// </summary>
public sealed class MaintenanceChecklistService : IMaintenanceChecklistService
{
    private const string ChecklistModule = "Maintenance Checklist"; // audit "Module" value: the menu name

    // Audit actions are "Checklist*", not "MaintenanceChecklist*": audit.audit_log.action is VARCHAR(30) and
    // "MaintenanceChecklistDeactivated" (31) does not fit - the audit row would be lost. Module + EntityName identify the master.
    private const int NameMaxLength = 150;                           // checklist_name NVARCHAR(150)
    private const int ItemLabelMaxLength = 200;                      // item_label NVARCHAR(200)
    private const string AtLeastOneItemMessage = "Please add at least one checklist item."; // analysis 4.10 / validation table
    private const string NotAPlanMessage = "This is a Checklist Master, not a preventive maintenance plan. Edit it from Checklist Masters.";
    private const string NotAMasterMessage = "This is a preventive maintenance plan, not a Checklist Master. Edit it from Preventive Maintenance Plans.";

    private readonly IMaintenanceChecklistRepository _repository;
    private readonly IMachineRepository _machineRepository;
    private readonly IMaintenanceTypeRepository _maintenanceTypeRepository;
    private readonly IUserRepository _userRepository;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly IAuditLogService _auditLogService;
    private readonly INotificationPublisher _notificationPublisher;
    private readonly ILogger<MaintenanceChecklistService> _logger;

    public MaintenanceChecklistService(
        IMaintenanceChecklistRepository repository,
        IMachineRepository machineRepository,
        IMaintenanceTypeRepository maintenanceTypeRepository,
        IUserRepository userRepository,
        IDateTimeProvider dateTimeProvider,
        IAuditLogService auditLogService,
        INotificationPublisher notificationPublisher,
        ILogger<MaintenanceChecklistService> logger)
    {
        _repository = repository;
        _machineRepository = machineRepository;
        _maintenanceTypeRepository = maintenanceTypeRepository;
        _userRepository = userRepository;
        _dateTimeProvider = dateTimeProvider;
        _auditLogService = auditLogService;
        _notificationPublisher = notificationPublisher;
        _logger = logger;
    }

    public async Task<PagedResult<MaintenanceChecklistDto>> GetAllAsync(MaintenanceChecklistListQuery query, CancellationToken cancellationToken)
    {
        var (items, totalCount) = await _repository.GetAllAsync(query, checklistMasters: false, cancellationToken);

        return PagedResult<MaintenanceChecklistDto>.Create(items.Select(MapToDto).ToList(), query.PageNumber, query.PageSize, totalCount);
    }

    public async Task<PagedResult<MaintenanceChecklistDto>> GetChecklistMastersAsync(MaintenanceChecklistListQuery query, CancellationToken cancellationToken)
    {
        var (items, totalCount) = await _repository.GetAllAsync(query, checklistMasters: true, cancellationToken);

        return PagedResult<MaintenanceChecklistDto>.Create(items.Select(MapToDto).ToList(), query.PageNumber, query.PageSize, totalCount);
    }

    public async Task<IReadOnlyList<ChecklistMasterOptionDto>> GetChecklistMasterOptionsAsync(string? appliesTo, CancellationToken cancellationToken)
    {
        var input = string.IsNullOrWhiteSpace(appliesTo) ? MaintenanceChecklistAppliesTo.Machine : appliesTo.Trim();
        var normalized = MaintenanceChecklistAppliesTo.All.FirstOrDefault(a => string.Equals(a, input, StringComparison.OrdinalIgnoreCase))
            ?? throw new ValidationException($"AppliesTo must be one of: {string.Join(", ", MaintenanceChecklistAppliesTo.All)}.");

        var masters = await _repository.GetActiveChecklistMastersAsync(normalized, cancellationToken);

        return masters.Select(m => new ChecklistMasterOptionDto
        {
            ChecklistId = m.ChecklistId,
            ChecklistCode = m.ChecklistCode,
            ChecklistName = m.ChecklistName,
            AppliesTo = m.AppliesTo,
            Items = MapItems(m.Items),
        }).ToList();
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
        // A new checklist is always active, so its recurring configuration is always required - and a new Machine plan
        // always uses a Checklist Master (migration 020).
        var (name, appliesTo, frequency, machineId, startDate, sourceId, labels) = NormalizeAndValidate(
            request, isActive: true, ownItemsAllowedForMachine: false, rowVersion: null, requireRowVersion: false, out _);

        await EnsureNameIsFreeAsync(name, excludeId: null, cancellationToken);

        var machine = await ResolveMachineAsync(machineId, currentMachineId: null, cancellationToken);
        var maintenanceType = await ResolveMaintenanceTypeAsync(request.MaintenanceTypeId, currentTypeId: null, cancellationToken);
        var source = await ResolveChecklistMasterAsync(sourceId, currentSourceId: null, appliesTo, cancellationToken);

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
            MaintenanceTypeId = maintenanceType?.MaintenanceTypeId, // copied onto every PM occurrence (migration 017)
            SourceChecklistId = source?.ChecklistId, // the navigation stays unset so the insert never touches the master row
            IsActive = true, // a new checklist is always active - see CreateMaintenanceChecklistRequest
            CreatedAt = now,
            CreatedBy = actingUserId,
            Items = source is null ? BuildItems(labels, now, actingUserId) : new List<MaintenanceChecklistItem>(), // never a copy of the master's
        };

        // Filled in by the plan inside the transaction; read only after it has committed.
        MachinePm? firstOccurrence = null;
        DateOnly? machineNextBefore = null;
        DateOnly? machineNextAfter = null;

        var plan = appliesTo == MaintenanceChecklistAppliesTo.Machine
            ? new MachinePmOccurrencePlan
            {
                // The first occurrence is due ON the start date (even one in the past: it is then simply overdue).
                BuildOccurrence = saved => firstOccurrence = MachinePmOccurrenceFactory.NewOccurrence(
                    saved, source?.Items ?? saved.Items, saved.StartDate!.Value, now, actingUserId),
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
        created.MaintenanceType = maintenanceType;
        created.SourceChecklist = source;

        await WriteAuditAsync(
            "ChecklistCreated", created, actingUserId, ipAddress,
            actor => $"{actor} created {created.Frequency?.ToLowerInvariant()} checklist '{created.ChecklistName}' ({created.ChecklistCode}) for " +
                     (machine is null ? created.AppliesTo : $"machine {machine.MachineCode}") +
                     (created.StartDate is { } start ? $" starting {start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}" : string.Empty) +
                     (source is null
                         ? $" with {Pluralize(created.Items.Count)}."
                         : $" using checklist master {source.ChecklistCode} ({Pluralize(source.Items.Count)})."),
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

        if (checklist.IsChecklistMaster)
        {
            throw new ValidationException(NotAPlanMessage);
        }

        // The active-checklist requirements follow the checklist's stored status: an inactive legacy checklist may keep an
        // incomplete machine / start date (CK_maintenance_checklist_master_active_config). A Machine plan created before
        // migration 020 may keep its own items until a Checklist Master is chosen (no automatic conversion).
        var keepsOwnItems = checklist.AppliesTo == MaintenanceChecklistAppliesTo.Machine && checklist.SourceChecklistId is null;
        var (name, appliesTo, frequency, machineId, startDate, sourceId, labels) = NormalizeAndValidate(
            request, checklist.IsActive, ownItemsAllowedForMachine: keepsOwnItems, request.RowVersion, requireRowVersion: true, out var originalRowVersion);

        await EnsureNameIsFreeAsync(name, checklistId, cancellationToken);

        var machine = await ResolveMachineAsync(machineId, checklist.MachineId, cancellationToken);
        var maintenanceType = await ResolveMaintenanceTypeAsync(request.MaintenanceTypeId, checklist.MaintenanceTypeId, cancellationToken);
        var source = await ResolveChecklistMasterAsync(sourceId, checklist.SourceChecklistId, appliesTo, cancellationToken);
        var sourceChanged = checklist.SourceChecklistId != source?.ChecklistId;

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

        if (checklist.MaintenanceTypeId != maintenanceType?.MaintenanceTypeId)
        {
            details.Add(new AuditLogDetailEntry("maintenance_type", checklist.MaintenanceType?.MaintenanceTypeName, maintenanceType?.MaintenanceTypeName));
        }

        if (sourceChanged)
        {
            details.Add(new AuditLogDetailEntry("checklist_master", checklist.SourceChecklist?.ChecklistCode, source?.ChecklistCode));
        }

        // Items are rewritten only if the list (labels and order) actually changed, so an edit that only renames the
        // checklist keeps the item ids PM history points at. The audit records the item count, not the labels: the
        // joined labels could exceed audit_log_detail's 1000-character value columns.
        // With a Checklist Master the plan keeps NO items of its own (an older plan's own items are removed when one is
        // chosen), so the master's items are never duplicated onto the plan.
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
        checklist.MaintenanceTypeId = maintenanceType?.MaintenanceTypeId; // open PMs follow it, untyped completed ones get it
        checklist.SourceChecklistId = source?.ChecklistId;
        checklist.Machine = null; // never attached by the update: only machine_id is written
        checklist.MaintenanceType = null;
        checklist.SourceChecklist = null; // only source_checklist_id is written - the master row is never touched
        checklist.UpdatedAt = now;
        checklist.UpdatedBy = actingUserId;
        if (itemsChanged)
        {
            checklist.Items = BuildItems(labels, now, actingUserId);
        }

        // The open occurrence's snapshot follows the plan's EFFECTIVE items: a different master counts as an item change.
        var sync = new OccurrenceSyncResult();
        var plan = BuildOccurrenceSyncPlan(
            checklist, before, itemsChanged || sourceChanged, () => source?.Items ?? checklist.Items, now, actingUserId, sync);

        var updated = await _repository.UpdateAsync(checklist, originalRowVersion!, itemsChanged, plan, cancellationToken);
        updated.Machine = machine;
        updated.MaintenanceType = maintenanceType;
        updated.SourceChecklist = source;

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
            actor => $"{actor} deactivated {(deactivated.IsChecklistMaster ? "checklist master" : "checklist")} '{deactivated.ChecklistName}' ({deactivated.ChecklistCode}).",
            new[] { new AuditLogDetailEntry("is_active", "True", "False") }, cancellationToken);

        return MapToDto(deactivated);
    }

    public async Task<MaintenanceChecklistDto> CreateChecklistMasterAsync(
        CreateChecklistMasterRequest request, int? actingUserId, string? ipAddress, CancellationToken cancellationToken)
    {
        var (name, appliesTo, labels) = NormalizeAndValidateMaster(request, rowVersion: null, requireRowVersion: false, out _);

        await EnsureNameIsFreeAsync(name, excludeId: null, cancellationToken);

        var now = _dateTimeProvider.UtcNow;
        var master = new MaintenanceChecklist
        {
            // No plan configuration at all - that is what makes the row a checklist master (migration 020).
            ChecklistName = name,
            AppliesTo = appliesTo,
            IsActive = true,
            CreatedAt = now,
            CreatedBy = actingUserId,
            Items = BuildItems(labels, now, actingUserId),
        };

        var created = await _repository.AddAsync(master, firstOccurrence: null, cancellationToken); // a master never schedules a PM

        await WriteAuditAsync(
            "ChecklistCreated", created, actingUserId, ipAddress,
            actor => $"{actor} created checklist master '{created.ChecklistName}' ({created.ChecklistCode}) for {created.AppliesTo} with {Pluralize(created.Items.Count)}.",
            details: null, cancellationToken);

        return MapToDto(created);
    }

    public async Task<MaintenanceChecklistDto> UpdateChecklistMasterAsync(
        int checklistId, UpdateChecklistMasterRequest request, int? actingUserId, string? ipAddress, CancellationToken cancellationToken)
    {
        var master = await _repository.GetByIdAsync(checklistId, cancellationToken)
            ?? throw new NotFoundException(nameof(MaintenanceChecklist), checklistId);

        if (!master.IsChecklistMaster)
        {
            throw new ValidationException(NotAMasterMessage);
        }

        var (name, appliesTo, labels) = NormalizeAndValidateMaster(request, request.RowVersion, requireRowVersion: true, out var originalRowVersion);

        await EnsureNameIsFreeAsync(name, checklistId, cancellationToken);

        var details = new List<AuditLogDetailEntry>();
        if (!string.Equals(master.ChecklistName, name, StringComparison.Ordinal))
        {
            details.Add(new AuditLogDetailEntry("checklist_name", master.ChecklistName, name));
        }

        if (!string.Equals(master.AppliesTo, appliesTo, StringComparison.Ordinal))
        {
            // The plans that use it are Machine plans: a master they use must keep applying to Machine.
            if (await _repository.IsChecklistMasterUsedAsync(checklistId, cancellationToken))
            {
                throw new ConflictException("The Checklist Master is used by preventive maintenance plans, so its Applies To cannot be changed.");
            }

            details.Add(new AuditLogDetailEntry("applies_to", master.AppliesTo, appliesTo));
        }

        // Replaced as a set only when labels/order changed (keeps the item ids PM snapshots point at). The plans that use the
        // master copy the new items into the PM occurrences generated from now on; existing PMs keep their snapshot.
        var currentLabels = master.Items.OrderBy(i => i.SortOrder).ThenBy(i => i.ChecklistItemId).Select(i => i.ItemLabel).ToList();
        var itemsChanged = !currentLabels.SequenceEqual(labels, StringComparer.Ordinal);
        if (itemsChanged)
        {
            details.Add(new AuditLogDetailEntry("items", Pluralize(currentLabels.Count), Pluralize(labels.Count)));
        }

        var now = _dateTimeProvider.UtcNow;
        master.ChecklistName = name;
        master.AppliesTo = appliesTo;
        master.UpdatedAt = now;
        master.UpdatedBy = actingUserId;
        if (itemsChanged)
        {
            master.Items = BuildItems(labels, now, actingUserId);
        }

        var updated = await _repository.UpdateAsync(master, originalRowVersion!, itemsChanged, occurrenceSync: null, cancellationToken);

        var changeSummary = details.Count > 0 ? $"Changed: {string.Join(", ", details.Select(d => d.FieldName))}." : "No field values changed.";
        await WriteAuditAsync(
            "ChecklistUpdated", updated, actingUserId, ipAddress,
            actor => $"{actor} updated checklist master '{updated.ChecklistName}' ({updated.ChecklistCode}). {changeSummary}",
            details, cancellationToken);

        return MapToDto(updated);
    }

    // Migration 020. Unknown -> 404; not a checklist master, another asset type or no items -> 400; a NEWLY chosen master must
    // be active (400). An unchanged master deactivated since stays assigned - the project's convention for references.
    private async Task<MaintenanceChecklist?> ResolveChecklistMasterAsync(int? sourceId, int? currentSourceId, string appliesTo, CancellationToken cancellationToken)
    {
        if (sourceId is not { } id)
        {
            return null;
        }

        var master = await _repository.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException("Checklist Master", id);

        if (!master.IsChecklistMaster)
        {
            throw new ValidationException("The selected checklist is not a Checklist Master.");
        }

        if (!string.Equals(master.AppliesTo, appliesTo, StringComparison.Ordinal))
        {
            throw new ValidationException($"The selected Checklist Master does not apply to {appliesTo}.");
        }

        if (!master.IsActive && id != currentSourceId)
        {
            throw new ValidationException("The selected Checklist Master is not active.");
        }

        if (master.Items.Count == 0)
        {
            throw new ValidationException("The selected Checklist Master has no checklist items.");
        }

        return master;
    }

    private static (string Name, string AppliesTo, IReadOnlyList<string> Labels) NormalizeAndValidateMaster(
        CreateChecklistMasterRequest request, string? rowVersion, bool requireRowVersion, out byte[]? originalRowVersion)
    {
        var (name, _, appliesTo, labels, errors) = NormalizeCommon(request.ChecklistName, request.AppliesTo, request.Items);
        ValidateLabels(labels, errors);

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

        return (name, appliesTo!, labels);
    }

    // The first occurrence of an EXISTING checklist that becomes a Machine checklist (Mold -> Machine): the first cycle date
    // of its start date on or after today (plant date) - never a backlog of past cycle dates.
    private MachinePm BuildFirstOccurrence(MaintenanceChecklist saved, IEnumerable<MaintenanceChecklistItem> items, DateTime now, int? actingUserId)
    {
        var dueDate = RecurrenceRules.FirstOnOrAfter(saved.CycleAnchor(), saved.Frequency!, _dateTimeProvider.Today);

        return MachinePmOccurrenceFactory.NewOccurrence(saved, items, dueDate, now, actingUserId);
    }

    // Function 4: what an edit of an ACTIVE checklist does to its open occurrence and the machines involved. Null when
    // nothing about the occurrence can change (an inactive checklist, a Mold-only edit, or only the name changed).
    // effectiveItems: the plan's items once saved - its checklist master's, or its own (read inside the transaction, after
    // own items were rewritten, so the snapshot carries their new ids).
    private ChecklistOccurrenceSyncPlan? BuildOccurrenceSyncPlan(
        MaintenanceChecklist saved, (string AppliesTo, string? Frequency, int? MachineId, DateOnly? StartDate) before, bool itemsChanged,
        Func<IEnumerable<MaintenanceChecklistItem>> effectiveItems, DateTime now, int? actingUserId, OccurrenceSyncResult result)
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

                    result.NewOccurrence = BuildFirstOccurrence(checklist, effectiveItems(), now, actingUserId);
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
                    snapshot = MachinePmOccurrenceFactory.Snapshot(effectiveItems());
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

        // Migration 021: every new occurrence of a plan (first one, or one created by an edit) is notified, after commit.
        await MachinePmNotifications.ScheduledAsync(_notificationPublisher, occurrence.MachinePmId, occurrence.PmNo, machine.MachineCode, occurrence.ScheduledDate, actingUserId, cancellationToken);
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

    // Migration 017. Unknown -> 404; the type must apply to Machine or Both (400 - only Machine PMs carry a type); a NEWLY
    // chosen type must be active (400). An unchanged type deactivated since stays assigned, like the machine above.
    private async Task<MaintenanceType?> ResolveMaintenanceTypeAsync(int? maintenanceTypeId, int? currentTypeId, CancellationToken cancellationToken)
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

        if (!maintenanceType.IsActive && id != currentTypeId)
        {
            throw new ValidationException("The selected maintenance type is not active.");
        }

        return maintenanceType;
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
    // ownItemsAllowedForMachine: a Machine plan created before migration 020 that has no Checklist Master yet may keep its
    // own items (no automatic conversion); every other Machine plan must use a Checklist Master.
    private static (string Name, string AppliesTo, string? Frequency, int? MachineId, DateOnly? StartDate, int? SourceChecklistId, IReadOnlyList<string> Labels) NormalizeAndValidate(
        CreateMaintenanceChecklistRequest request, bool isActive, bool ownItemsAllowedForMachine, string? rowVersion, bool requireRowVersion, out byte[]? originalRowVersion)
    {
        var (name, appliesToInput, appliesTo, labels, errors) = NormalizeCommon(request.ChecklistName, request.AppliesTo, request.Items);
        var frequencyInput = string.IsNullOrWhiteSpace(request.Frequency) ? null : request.Frequency.Trim();
        var frequency = ChecklistFrequency.All.FirstOrDefault(f => string.Equals(f, frequencyInput, StringComparison.OrdinalIgnoreCase));

        // CK_maintenance_checklist_master_frequency / _active_config. Migration 020: a plan ALWAYS has a frequency, also when
        // inactive - a row without one is a Checklist Master.
        if (frequencyInput is null)
        {
            errors.Add("Frequency is required.");
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

        // Migration 017: only Machine PMs have a maintenance type.
        if (request.MaintenanceTypeId is <= 0)
        {
            errors.Add("MaintenanceTypeId is not valid.");
        }
        else if (appliesTo == MaintenanceChecklistAppliesTo.Mold && request.MaintenanceTypeId is not null)
        {
            errors.Add("A Mold checklist cannot have a maintenance type.");
        }

        // CK_maintenance_checklist_master_start_date: the anchor of an active Machine checklist's cycle.
        if (appliesTo == MaintenanceChecklistAppliesTo.Machine && request.StartDate is null && isActive)
        {
            errors.Add("StartDate is required for a Machine checklist.");
        }

        // Migration 020: where the plan's items come from. A Machine plan selects a Checklist Master and then has NO items
        // of its own - arbitrary items (or item ids) are never accepted next to it; a Mold plan (and an older Machine plan
        // without a master) keeps its own items.
        var usesOwnItems = appliesTo != MaintenanceChecklistAppliesTo.Machine || ownItemsAllowedForMachine; // Mold (or not yet valid), or an older Machine plan
        if (request.SourceChecklistId is <= 0)
        {
            errors.Add("SourceChecklistId is not valid.");
        }
        else if (appliesTo == MaintenanceChecklistAppliesTo.Mold && request.SourceChecklistId is not null)
        {
            errors.Add("A Mold plan cannot use a Checklist Master.");
        }
        else if (request.SourceChecklistId is not null)
        {
            if (labels.Count > 0) errors.Add("Checklist items come from the selected Checklist Master and cannot be entered on the plan.");
        }
        else if (appliesTo == MaintenanceChecklistAppliesTo.Machine && !usesOwnItems)
        {
            errors.Add("Checklist Master is required for a Machine plan.");
        }
        else if (usesOwnItems)
        {
            ValidateLabels(labels, errors);
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

        return (name, appliesTo!, frequency, request.MachineId, request.StartDate, request.SourceChecklistId, labels);
    }

    // Name, applies-to and item labels - the checklist rules shared by plans and Checklist Masters (analysis 4.10).
    private static (string Name, string? AppliesToInput, string? AppliesTo, List<string> Labels, List<string> Errors) NormalizeCommon(
        string? checklistName, string? appliesToValue, IReadOnlyList<MaintenanceChecklistItemRequest>? items)
    {
        var name = (checklistName ?? string.Empty).Trim();
        var appliesToInput = string.IsNullOrWhiteSpace(appliesToValue) ? null : appliesToValue.Trim();
        var appliesTo = MaintenanceChecklistAppliesTo.All.FirstOrDefault(a => string.Equals(a, appliesToInput, StringComparison.OrdinalIgnoreCase));
        var labels = (items ?? Array.Empty<MaintenanceChecklistItemRequest>())
            .Select(i => (i?.ItemLabel ?? string.Empty).Trim())
            .Where(l => l.Length > 0)
            .ToList();

        var errors = new List<string>();

        if (name.Length == 0) errors.Add("ChecklistName is required.");
        else if (name.Length > NameMaxLength) errors.Add($"ChecklistName must be at most {NameMaxLength} characters.");

        if (appliesToInput is null) errors.Add("AppliesTo is required.");
        else if (appliesTo is null) errors.Add($"AppliesTo must be one of: {string.Join(", ", MaintenanceChecklistAppliesTo.All)}.");

        return (name, appliesToInput, appliesTo, labels, errors);
    }

    private static void ValidateLabels(IReadOnlyList<string> labels, List<string> errors)
    {
        if (labels.Count == 0)
        {
            errors.Add(AtLeastOneItemMessage);
            return;
        }

        for (var i = 0; i < labels.Count; i++)
        {
            if (labels[i].Length > ItemLabelMaxLength)
            {
                errors.Add($"Checklist item {i + 1} must be at most {ItemLabelMaxLength} characters.");
            }
        }
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
        MaintenanceTypeId = checklist.MaintenanceTypeId,
        MaintenanceTypeCode = checklist.MaintenanceType?.MaintenanceTypeCode,
        MaintenanceTypeName = checklist.MaintenanceType?.MaintenanceTypeName,
        MaintenanceTypeIsActive = checklist.MaintenanceType?.IsActive,
        IsChecklistMaster = checklist.IsChecklistMaster,
        SourceChecklistId = checklist.SourceChecklistId,
        SourceChecklistCode = checklist.SourceChecklist?.ChecklistCode,
        SourceChecklistName = checklist.SourceChecklist?.ChecklistName,
        SourceChecklistIsActive = checklist.SourceChecklist?.IsActive,
        IsActive = checklist.IsActive,
        // A plan with a Checklist Master shows the MASTER's items (it has none of its own).
        Items = MapItems(checklist.SourceChecklistId is null ? checklist.Items : checklist.SourceChecklist?.Items ?? checklist.Items),
        CreatedAt = checklist.CreatedAt,
        CreatedBy = checklist.CreatedBy,
        UpdatedAt = checklist.UpdatedAt,
        UpdatedBy = checklist.UpdatedBy,
        RowVersion = Convert.ToBase64String(checklist.RowVersion),
    };

    private static List<MaintenanceChecklistItemDto> MapItems(IEnumerable<MaintenanceChecklistItem> items) =>
        items
            .OrderBy(i => i.SortOrder).ThenBy(i => i.ChecklistItemId)
            .Select(i => new MaintenanceChecklistItemDto { ChecklistItemId = i.ChecklistItemId, SortOrder = i.SortOrder, ItemLabel = i.ItemLabel })
            .ToList();
}
