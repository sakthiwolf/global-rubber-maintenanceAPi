using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Domain.Constants;
using GlobalRubber.MMM.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GlobalRubber.MMM.Tests;

/// <summary>Shared fakes for the maintenance checklist tests (service level and HTTP level).</summary>
internal static class MaintenanceChecklistTestData
{
    // 1 Machine Lubrication (Machine, active, Daily, machine 1 MAC-0001, 2 items), 2 Mold Cleaning (Mold, active, Weekly,
    // no machine, 1 item), 3 Old Checklist (Machine, INACTIVE legacy: no frequency, no machine - like the live CHK-0001).
    // Machines are MachineTestData's: 1, 2 active; 3 inactive.
    public static List<MaintenanceChecklist> Checklists() => new()
    {
        Checklist(1, "CHK-0001", "Machine Lubrication", "Machine", true, "Daily", 1, "Oil level checked", "Grease points lubricated"),
        Checklist(2, "CHK-0002", "Mold Cleaning", "Mold", true, "Weekly", null, "Cavities cleaned"),
        Checklist(3, "CHK-0003", "Old Checklist", "Machine", false, null, null, "Legacy step"),
    };

    private static MaintenanceChecklist Checklist(int id, string code, string name, string appliesTo, bool active, string? frequency, int? machineId, params string[] labels) => new()
    {
        ChecklistId = id, ChecklistCode = code, ChecklistName = name, AppliesTo = appliesTo, IsActive = active,
        Frequency = frequency, MachineId = machineId,
        StartDate = active ? new DateOnly(2026, 1, id) : null, // an active checklist's anchor = its creation date, as migration 013 backfilled
        CreatedAt = new DateTime(2026, 1, id, 0, 0, 0, DateTimeKind.Utc), CreatedBy = 1, RowVersion = new byte[] { 1 },
        Items = labels.Select((l, i) => new MaintenanceChecklistItem
        {
            ChecklistItemId = id * 10 + i, ChecklistId = id, SortOrder = i + 1, ItemLabel = l,
            CreatedAt = new DateTime(2026, 1, id, 0, 0, 0, DateTimeKind.Utc), CreatedBy = 1,
        }).ToList(),
    };
}

/// <summary>
/// Behaves like the real MaintenanceChecklistRepository where it matters: detached copies on read, next CHK-NNNN code on
/// add, Update writes only the header columns the real one writes (plus the items only when asked to replace them) and
/// Deactivate only is_active - and only if the row version still matches.
/// </summary>
internal sealed class InMemoryMaintenanceChecklistRepository : IMaintenanceChecklistRepository
{
    private const string ConcurrencyMessage = "The checklist was modified by another user. Refresh the checklist and try again.";

    private readonly List<MaintenanceChecklist> _checklists;
    private readonly List<Machine> _machines;
    private int _nextCode;
    private int _nextItemId = 1000;
    private int _nextPmLineId = 5000;

    public InMemoryMaintenanceChecklistRepository(List<MaintenanceChecklist> checklists, List<Machine>? machines = null, List<MachinePm>? pms = null)
    {
        _checklists = checklists;
        _machines = machines ?? MachineTestData.Machines();
        Pms = pms ?? new List<MachinePm>();
        PmSequence = Pms.Count; // MACHINE_PM last_number: the seeded occurrences are MPM-0001..
        _nextCode = checklists.Count + 1;
    }

    /// <summary>Runs at the start of Update/Deactivate - lets a test play "another user saved first".</summary>
    public Action<int>? BeforeWrite { get; set; }
    public int AddCalls { get; private set; }

    /// <summary>Machine PM occurrences written by checklist creation (plus any seeded ones), and the MACHINE_PM sequence.</summary>
    public List<MachinePm> Pms { get; }
    public int PmSequence { get; private set; }

    /// <summary>Makes the next create fail like a database error AFTER the checklist rows were written (at the PM insert).</summary>
    public bool FailAtOccurrenceInsert { get; set; }

    /// <summary>Makes the next create/update fail like a database error at the machine update (after the PM rows were written).</summary>
    public bool FailAtMachineUpdate { get; set; }

    /// <summary>Makes the next update fail like a database error while writing the open occurrence (after the checklist rows).</summary>
    public bool FailAtOccurrenceSync { get; set; }

    /// <summary>The machine ids the last update locked, in the order it locked them.</summary>
    public IReadOnlyList<int> LastLockOrder { get; private set; } = Array.Empty<int>();

    public MachinePm StoredPm(string pmNo) => Pms.Single(p => p.PmNo == pmNo);

    private static MachinePm ClonePm(MachinePm p) => new()
    {
        MachinePmId = p.MachinePmId, PmNo = p.PmNo, MachineId = p.MachineId, MaintenanceTypeId = p.MaintenanceTypeId, ScheduledDate = p.ScheduledDate,
        CompletedDate = p.CompletedDate, EngineerId = p.EngineerId, ChecklistId = p.ChecklistId, Remarks = p.Remarks, MaintenanceBy = p.MaintenanceBy,
        Status = p.Status, CreatedAt = p.CreatedAt, CreatedBy = p.CreatedBy, UpdatedAt = p.UpdatedAt, UpdatedBy = p.UpdatedBy,
        RowVersion = (byte[])p.RowVersion.Clone(),
        ChecklistItems = p.ChecklistItems.Select(l => new MachinePmChecklistItem
        {
            MachinePmChecklistId = l.MachinePmChecklistId, MachinePmId = l.MachinePmId, SortOrder = l.SortOrder, ItemLabel = l.ItemLabel,
            ChecklistItemId = l.ChecklistItemId, IsChecked = l.IsChecked,
        }).ToList(),
    };

    private static bool IsOpen(MachinePm p) => p.Status is MachinePmStatus.Scheduled or MachinePmStatus.InProgress;

    public Machine StoredMachine(int id) => _machines.Single(m => m.MachineId == id);
    public int UpdateCalls { get; private set; }
    public int DeactivateCalls { get; private set; }
    public bool? LastReplaceItems { get; private set; }
    public int Count => _checklists.Count;

    public MaintenanceChecklist Stored(int id) => _checklists.Single(c => c.ChecklistId == id);
    public void SimulateConcurrentModification(int id) => Stored(id).RowVersion = Next(Stored(id).RowVersion);

    private static MaintenanceChecklistItem Clone(MaintenanceChecklistItem i) => new()
    {
        ChecklistItemId = i.ChecklistItemId, ChecklistId = i.ChecklistId, SortOrder = i.SortOrder, ItemLabel = i.ItemLabel,
        CreatedAt = i.CreatedAt, CreatedBy = i.CreatedBy, UpdatedAt = i.UpdatedAt, UpdatedBy = i.UpdatedBy,
    };

    private MaintenanceChecklist Clone(MaintenanceChecklist c) => new()
    {
        ChecklistId = c.ChecklistId, ChecklistCode = c.ChecklistCode, ChecklistName = c.ChecklistName, AppliesTo = c.AppliesTo,
        Frequency = c.Frequency, MachineId = c.MachineId, StartDate = c.StartDate,
        Machine = c.MachineId is { } machineId ? _machines.Single(m => m.MachineId == machineId) : null,
        IsActive = c.IsActive, CreatedAt = c.CreatedAt, CreatedBy = c.CreatedBy, UpdatedAt = c.UpdatedAt, UpdatedBy = c.UpdatedBy,
        RowVersion = (byte[])c.RowVersion.Clone(),
        Items = c.Items.OrderBy(i => i.SortOrder).Select(Clone).ToList(),
    };

    private static byte[] Next(byte[] current) => new[] { (byte)(current.Length == 0 ? 1 : current[0] + 1) };

    public Task<(IReadOnlyList<MaintenanceChecklist> Items, int TotalCount)> GetAllAsync(MaintenanceChecklistListQuery query, CancellationToken cancellationToken)
    {
        IEnumerable<MaintenanceChecklist> q = _checklists;
        if (query.IsActive is { } active) q = q.Where(c => c.IsActive == active);
        if (query.MachineId is { } machineId) q = q.Where(c => c.MachineId == machineId);
        if (!string.IsNullOrWhiteSpace(query.Frequency)) q = q.Where(c => c.Frequency == query.Frequency.Trim());
        if (!string.IsNullOrWhiteSpace(query.AppliesTo)) q = q.Where(c => c.AppliesTo == query.AppliesTo.Trim());
        var search = query.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            q = q.Where(c => c.ChecklistCode.Contains(search, StringComparison.OrdinalIgnoreCase)
                             || c.ChecklistName.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        var all = q.OrderBy(c => c.ChecklistName).ThenBy(c => c.ChecklistId).ToList();
        var page = all.Skip((query.PageNumber - 1) * query.PageSize).Take(query.PageSize).Select(Clone).ToList();
        return Task.FromResult<(IReadOnlyList<MaintenanceChecklist>, int)>((page, all.Count));
    }

    public Task<MaintenanceChecklist?> GetByIdAsync(int checklistId, CancellationToken cancellationToken)
    {
        var stored = _checklists.FirstOrDefault(c => c.ChecklistId == checklistId);
        return Task.FromResult(stored is null ? null : Clone(stored));
    }

    public Task<bool> ExistsActiveByNameAsync(string name, int? excludeChecklistId, CancellationToken cancellationToken) =>
        Task.FromResult(_checklists.Any(c =>
            c.IsActive && c.ChecklistId != excludeChecklistId && string.Equals(c.ChecklistName, name, StringComparison.OrdinalIgnoreCase)));

    public Task<MaintenanceChecklistLookupsDto> GetLookupsAsync(CancellationToken cancellationToken) => Task.FromResult(new MaintenanceChecklistLookupsDto
    {
        Machines = _machines.Where(m => m.IsActive).OrderBy(m => m.MachineCode)
            .Select(m => new MaintenanceChecklistMachineLookupDto { MachineId = m.MachineId, MachineCode = m.MachineCode, MachineName = m.MachineName })
            .ToList(),
    });

    // Behaves like the real repository's single transaction: every id, code and number is only TENTATIVE until the end;
    // a failure anywhere leaves the checklists, the PMs, the machines and BOTH sequences exactly as they were.
    public Task<MaintenanceChecklist> AddAsync(MaintenanceChecklist checklist, MachinePmOccurrencePlan? firstOccurrence, CancellationToken cancellationToken)
    {
        AddCalls++;
        var itemId = _nextItemId;
        checklist.ChecklistId = _checklists.Count == 0 ? 1 : _checklists.Max(c => c.ChecklistId) + 1;
        checklist.ChecklistCode = $"CHK-{_nextCode:0000}"; // the real repository issues this from the MAINTENANCE_CHECKLIST sequence
        checklist.RowVersion = new byte[] { 1 };
        foreach (var item in checklist.Items)
        {
            item.ChecklistItemId = itemId++;
            item.ChecklistId = checklist.ChecklistId;
        }

        MachinePm? occurrence = null;
        Machine? locked = null;
        if (firstOccurrence is not null)
        {
            var stored = _machines.SingleOrDefault(m => m.MachineId == checklist.MachineId)
                ?? throw new NotFoundException(nameof(Machine), checklist.MachineId!);
            locked = new Machine { MachineId = stored.MachineId, MachineCode = stored.MachineCode, NextMaintenanceDate = stored.NextMaintenanceDate, UpdatedAt = stored.UpdatedAt, UpdatedBy = stored.UpdatedBy };

            occurrence = firstOccurrence.BuildOccurrence(checklist);
            if (FailAtOccurrenceInsert)
            {
                FailAtOccurrenceInsert = false;
                throw new DbUpdateException("Simulated database failure while inserting the PM occurrence.");
            }

            if (Pms.Any(p => p.ChecklistId == checklist.ChecklistId && p.Status is MachinePmStatus.Scheduled or MachinePmStatus.InProgress))
            {
                throw new ConflictException("The checklist already has an open maintenance occurrence."); // UX_machine_pm_transaction_open_occurrence
            }

            occurrence.MachinePmId = Pms.Count == 0 ? 1 : Pms.Max(p => p.MachinePmId) + 1;
            occurrence.PmNo = $"MPM-{PmSequence + 1:0000}"; // the real repository issues this from the MACHINE_PM sequence
            occurrence.RowVersion = new byte[] { 1 };

            var openDueDates = Pms.Where(p => p.MachineId == stored.MachineId && p.Status is MachinePmStatus.Scheduled or MachinePmStatus.InProgress)
                .Select(p => p.ScheduledDate).Append(occurrence.ScheduledDate).OrderBy(d => d).ToList();
            firstOccurrence.ApplyToLockedMachine(locked, openDueDates);
            if (FailAtMachineUpdate)
            {
                FailAtMachineUpdate = false;
                throw new DbUpdateException("Simulated database failure while updating the machine.");
            }
        }

        // "Commit".
        _nextCode++;
        _nextItemId = itemId;
        _checklists.Add(Clone(checklist));
        if (occurrence is not null)
        {
            PmSequence++;
            foreach (var line in occurrence.ChecklistItems)
            {
                line.MachinePmChecklistId = _nextPmLineId++;
                line.MachinePmId = occurrence.MachinePmId;
            }

            Pms.Add(occurrence);
            var stored = StoredMachine(locked!.MachineId);
            stored.NextMaintenanceDate = locked.NextMaintenanceDate;
            stored.UpdatedAt = locked.UpdatedAt;
            stored.UpdatedBy = locked.UpdatedBy;
        }

        return Task.FromResult(checklist);
    }

    // Behaves like the real repository's single transaction: the open occurrence and the machines are worked on as COPIES
    // and only written back at the end - a stale row version or any failure leaves checklists, items, PMs, machines and
    // the MACHINE_PM sequence exactly as they were.
    public Task<MaintenanceChecklist> UpdateAsync(
        MaintenanceChecklist checklist, byte[] originalRowVersion, bool replaceItems, ChecklistOccurrenceSyncPlan? occurrenceSync,
        CancellationToken cancellationToken)
    {
        UpdateCalls++;
        LastReplaceItems = replaceItems;
        BeforeWrite?.Invoke(checklist.ChecklistId);

        var stored = _checklists.FirstOrDefault(c => c.ChecklistId == checklist.ChecklistId);
        if (stored is null || !stored.RowVersion.SequenceEqual(originalRowVersion))
        {
            throw new ConflictException(ConcurrencyMessage);
        }

        var itemId = _nextItemId;
        if (replaceItems)
        {
            foreach (var item in checklist.Items)
            {
                item.ChecklistItemId = itemId++;
                item.ChecklistId = checklist.ChecklistId;
            }
        }

        MachinePm? openStored = null, openCopy = null, newOccurrence = null;
        OpenOccurrenceChange change = OpenOccurrenceChange.None;
        var lockedCopies = new List<Machine>();
        if (occurrenceSync is not null)
        {
            openStored = Pms.SingleOrDefault(p => p.ChecklistId == checklist.ChecklistId && IsOpen(p));
            var toLock = occurrenceSync.MachineIdsToLock.ToHashSet();
            if (openStored is not null) toLock.Add(openStored.MachineId);
            LastLockOrder = toLock.OrderBy(id => id).ToList();
            foreach (var id in LastLockOrder)
            {
                var m = _machines.SingleOrDefault(x => x.MachineId == id) ?? throw new NotFoundException(nameof(Machine), id);
                lockedCopies.Add(new Machine { MachineId = m.MachineId, MachineCode = m.MachineCode, NextMaintenanceDate = m.NextMaintenanceDate, UpdatedAt = m.UpdatedAt, UpdatedBy = m.UpdatedBy });
            }

            openCopy = openStored is null ? null : ClonePm(openStored);
            change = occurrenceSync.Decide(checklist, openCopy);
            if (FailAtOccurrenceSync)
            {
                FailAtOccurrenceSync = false;
                throw new DbUpdateException("Simulated database failure while writing the open occurrence.");
            }

            if (change.NewOccurrence is not null)
            {
                newOccurrence = change.NewOccurrence;
                newOccurrence.MachinePmId = Pms.Count == 0 ? 1 : Pms.Max(p => p.MachinePmId) + 1;
                newOccurrence.PmNo = $"MPM-{PmSequence + 1:0000}"; // the real repository issues this from the MACHINE_PM sequence
                newOccurrence.RowVersion = new byte[] { 1 };
            }

            foreach (var machine in lockedCopies)
            {
                var openDueDates = Pms.Where(p => IsOpen(p) && p != openStored && p.MachineId == machine.MachineId).Select(p => p.ScheduledDate)
                    .Concat(openCopy is not null && openCopy.MachineId == machine.MachineId ? new[] { openCopy.ScheduledDate } : Array.Empty<DateOnly>())
                    .Concat(newOccurrence is not null && newOccurrence.MachineId == machine.MachineId ? new[] { newOccurrence.ScheduledDate } : Array.Empty<DateOnly>())
                    .OrderBy(d => d).ToList();
                occurrenceSync.ApplyToLockedMachine(machine, openDueDates);
            }

            if (FailAtMachineUpdate)
            {
                FailAtMachineUpdate = false;
                throw new DbUpdateException("Simulated database failure while updating the machine.");
            }
        }

        // "Commit" - exactly the columns the real UpdateAsync writes - never the code, IsActive or the creation columns.
        _nextItemId = itemId;
        stored.ChecklistName = checklist.ChecklistName;
        stored.AppliesTo = checklist.AppliesTo;
        stored.Frequency = checklist.Frequency;
        stored.MachineId = checklist.MachineId;
        stored.StartDate = checklist.StartDate;
        stored.UpdatedAt = checklist.UpdatedAt;
        stored.UpdatedBy = checklist.UpdatedBy;
        if (replaceItems)
        {
            stored.Items = checklist.Items.Select(Clone).ToList();
        }

        if (openStored is not null && openCopy is not null)
        {
            if (change.ReplacementSnapshot is not null)
            {
                openCopy.ChecklistItems = change.ReplacementSnapshot.Select(l =>
                {
                    l.MachinePmChecklistId = _nextPmLineId++;
                    l.MachinePmId = openCopy.MachinePmId;
                    return l;
                }).ToList();
            }

            Pms[Pms.IndexOf(openStored)] = openCopy;
        }

        if (newOccurrence is not null)
        {
            PmSequence++;
            foreach (var line in newOccurrence.ChecklistItems)
            {
                line.MachinePmChecklistId = _nextPmLineId++;
                line.MachinePmId = newOccurrence.MachinePmId;
            }

            Pms.Add(newOccurrence);
        }

        foreach (var copy in lockedCopies)
        {
            var machine = StoredMachine(copy.MachineId);
            machine.NextMaintenanceDate = copy.NextMaintenanceDate;
            machine.UpdatedAt = copy.UpdatedAt;
            machine.UpdatedBy = copy.UpdatedBy;
        }

        stored.RowVersion = Next(stored.RowVersion);
        checklist.RowVersion = stored.RowVersion;
        return Task.FromResult(checklist);
    }

    public Task<MaintenanceChecklist> DeactivateAsync(MaintenanceChecklist checklist, CancellationToken cancellationToken)
    {
        DeactivateCalls++;
        BeforeWrite?.Invoke(checklist.ChecklistId);

        var stored = _checklists.FirstOrDefault(c => c.ChecklistId == checklist.ChecklistId);
        if (stored is null || !stored.RowVersion.SequenceEqual(checklist.RowVersion))
        {
            throw new ConflictException(ConcurrencyMessage);
        }

        stored.IsActive = false;
        stored.UpdatedAt = checklist.UpdatedAt;
        stored.UpdatedBy = checklist.UpdatedBy;
        stored.RowVersion = Next(stored.RowVersion);
        checklist.RowVersion = stored.RowVersion;
        return Task.FromResult(checklist);
    }
}
