using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Domain.Common;
using GlobalRubber.MMM.Domain.Constants;
using GlobalRubber.MMM.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GlobalRubber.MMM.Tests;

/// <summary>A test clock whose instant can be moved; "today" is the plant (IST) date of that instant.</summary>
internal sealed class SettableClock : IDateTimeProvider
{
    public SettableClock(DateOnly plantToday) => SetToday(plantToday);

    public DateTime UtcNow { get; set; }
    public DateOnly Today => PlantTime.ToPlantDate(UtcNow);

    /// <summary>10:30 IST on the given plant date.</summary>
    public void SetToday(DateOnly plantToday) => UtcNow = plantToday.ToDateTime(new TimeOnly(5, 0), DateTimeKind.Utc);
}

/// <summary>
/// Builds the recurring-PM scenarios the Machine PM tests need: checklists (with their cycle anchor), occurrences and
/// completed history. Machines are MachineTestData's (1 MAC-0001 Running, 2 MAC-0002 Breakdown - active; 3 inactive).
/// </summary>
internal sealed class MachinePmScenario
{
    public List<Machine> Machines { get; } = MachineTestData.Machines();
    public List<MaintenanceChecklist> Checklists { get; } = new();
    public List<MachinePm> Pms { get; } = new();
    public int PmSequence { get; set; }

    /// <summary>
    /// An ACTIVE checklist whose cycle anchor (Start Date) is <paramref name="anchor"/>. It was created on another day on
    /// purpose, so every successor test also proves the start date - not the creation date - is the anchor.
    /// </summary>
    public MaintenanceChecklist Checklist(string frequency, DateOnly anchor, int? machineId = 1, string appliesTo = "Machine", bool active = true, params string[] labels)
    {
        var id = Checklists.Count + 1;
        var checklist = new MaintenanceChecklist
        {
            ChecklistId = id, ChecklistCode = $"CHK-{id:0000}", ChecklistName = $"{frequency} checklist {id}", AppliesTo = appliesTo,
            Frequency = frequency, MachineId = machineId, IsActive = active, StartDate = anchor,
            CreatedAt = new DateTime(2020, 6, 15, 4, 0, 0, DateTimeKind.Utc), // NOT the anchor
            RowVersion = new byte[] { 1 },
            Items = (labels.Length == 0 ? new[] { "Oil level checked", "Safety guard checked" } : labels)
                .Select((l, i) => new MaintenanceChecklistItem { ChecklistItemId = id * 100 + i, ChecklistId = id, SortOrder = i + 1, ItemLabel = l })
                .ToList(),
        };
        Checklists.Add(checklist);
        return checklist;
    }

    /// <summary>A Scheduled occurrence of the checklist (snapshot of its items as they are now), due on <paramref name="due"/>.</summary>
    public MachinePm Open(MaintenanceChecklist? checklist, DateOnly due, int? machineId = null) => Add(checklist, due, machineId, MachinePmStatus.Scheduled);

    /// <summary>A Completed occurrence (history), all items ticked.</summary>
    public MachinePm Completed(MaintenanceChecklist? checklist, DateOnly due, int? machineId = null)
    {
        var pm = Add(checklist, due, machineId, MachinePmStatus.Completed);
        pm.CompletedDate = due;
        pm.MaintenanceBy = "Ravi";
        pm.ChecklistItems.ForEach(l => l.IsChecked = true);
        return pm;
    }

    private MachinePm Add(MaintenanceChecklist? checklist, DateOnly due, int? machineId, string status)
    {
        PmSequence++;
        var id = Pms.Count + 1;
        var pm = new MachinePm
        {
            MachinePmId = id, PmNo = $"MPM-{PmSequence:0000}", MachineId = machineId ?? checklist?.MachineId ?? 1, ChecklistId = checklist?.ChecklistId,
            ScheduledDate = due, Status = status, CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), CreatedBy = 1, RowVersion = new byte[] { 1 },
            ChecklistItems = (checklist?.Items ?? new List<MaintenanceChecklistItem>()).OrderBy(i => i.SortOrder)
                .Select((i, n) => new MachinePmChecklistItem { MachinePmChecklistId = id * 100 + n, MachinePmId = id, SortOrder = i.SortOrder, ItemLabel = i.ItemLabel, ChecklistItemId = i.ChecklistItemId })
                .ToList(),
        };
        Pms.Add(pm);
        return pm;
    }
}

/// <summary>
/// Behaves like the real MachinePmRepository where it matters: detached copies, bucket filters on the given plant date,
/// and an ALL-OR-NOTHING completion - the PM, its ticks, the successor (MPM-NNNN from the MACHINE_PM sequence) and the
/// machine dates are only written back if the row version matches and nothing failed.
/// </summary>
internal sealed class InMemoryMachinePmRepository : IMachinePmRepository
{
    private readonly MachinePmScenario _data;
    private int _nextLineId = 9000;

    public InMemoryMachinePmRepository(MachinePmScenario data) => _data = data;

    public Action<int>? BeforeWrite { get; set; }
    public bool FailNextSave { get; set; }
    public int CompleteCalls { get; private set; }
    public IReadOnlyList<int> LastLockOrder { get; private set; } = Array.Empty<int>();
    public MachinePm Stored(int id) => _data.Pms.Single(p => p.MachinePmId == id);
    public MachinePm StoredByNo(string pmNo) => _data.Pms.Single(p => p.PmNo == pmNo);
    public Machine StoredMachine(int id) => _data.Machines.Single(m => m.MachineId == id);
    public void SimulateConcurrentModification(int id) => Stored(id).RowVersion = new[] { (byte)(Stored(id).RowVersion[0] + 1) };

    private static bool IsOpen(MachinePm p) => p.Status is MachinePmStatus.Scheduled or MachinePmStatus.InProgress;

    private MachinePm Clone(MachinePm p) => new()
    {
        MachinePmId = p.MachinePmId, PmNo = p.PmNo, MachineId = p.MachineId, MaintenanceTypeId = p.MaintenanceTypeId, ScheduledDate = p.ScheduledDate,
        CompletedDate = p.CompletedDate, EngineerId = p.EngineerId, ChecklistId = p.ChecklistId, Remarks = p.Remarks, MaintenanceBy = p.MaintenanceBy, Status = p.Status,
        CreatedAt = p.CreatedAt, CreatedBy = p.CreatedBy, UpdatedAt = p.UpdatedAt, UpdatedBy = p.UpdatedBy, RowVersion = (byte[])p.RowVersion.Clone(),
        Machine = StoredMachine(p.MachineId),
        Checklist = p.ChecklistId is { } c ? _data.Checklists.Single(x => x.ChecklistId == c) : null,
        ChecklistItems = p.ChecklistItems.OrderBy(i => i.SortOrder).Select(i => new MachinePmChecklistItem
        {
            MachinePmChecklistId = i.MachinePmChecklistId, MachinePmId = i.MachinePmId, SortOrder = i.SortOrder, ItemLabel = i.ItemLabel,
            ChecklistItemId = i.ChecklistItemId, IsChecked = i.IsChecked,
        }).ToList(),
    };

    private static MaintenanceChecklist CloneChecklist(MaintenanceChecklist c) => new()
    {
        ChecklistId = c.ChecklistId, ChecklistCode = c.ChecklistCode, ChecklistName = c.ChecklistName, AppliesTo = c.AppliesTo, Frequency = c.Frequency,
        MachineId = c.MachineId, StartDate = c.StartDate, IsActive = c.IsActive, CreatedAt = c.CreatedAt, RowVersion = (byte[])c.RowVersion.Clone(),
        Items = c.Items.OrderBy(i => i.SortOrder).Select(i => new MaintenanceChecklistItem { ChecklistItemId = i.ChecklistItemId, ChecklistId = i.ChecklistId, SortOrder = i.SortOrder, ItemLabel = i.ItemLabel }).ToList(),
    };

    private IEnumerable<MachinePm> Filtered(MachinePmListQuery q)
    {
        IEnumerable<MachinePm> all = _data.Pms;
        if (q.MachineId is { } machineId) all = all.Where(p => p.MachineId == machineId);
        var search = q.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            all = all.Where(p => p.PmNo.Contains(search, StringComparison.OrdinalIgnoreCase)
                                 || StoredMachine(p.MachineId).MachineCode.Contains(search, StringComparison.OrdinalIgnoreCase)
                                 || StoredMachine(p.MachineId).MachineName.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        return all;
    }

    private string? FrequencyOf(MachinePm p) => p.ChecklistId is { } c ? _data.Checklists.Single(x => x.ChecklistId == c).Frequency : null;

    // Like the real repository: DUE open PMs (scheduled on or before today) by their checklist's frequency (overdue
    // included), completed PMs together.
    private bool InBucket(MachinePm p, string bucket, DateOnly today) => bucket == MachinePmBucket.Completed
        ? p.Status == MachinePmStatus.Completed
        : p.Status != MachinePmStatus.Completed && p.ScheduledDate <= today && FrequencyOf(p) == MachinePmBucket.FrequencyOf(bucket);

    public Task<(IReadOnlyList<MachinePm> Items, int TotalCount)> GetAllAsync(MachinePmListQuery query, DateOnly today, CancellationToken cancellationToken)
    {
        var filtered = Filtered(query).Where(p => query.Bucket is null || InBucket(p, query.Bucket, today));
        var ordered = (query.Bucket == MachinePmBucket.Completed
            ? filtered.OrderByDescending(p => p.CompletedDate).ThenByDescending(p => p.MachinePmId)
            : filtered.OrderBy(p => p.ScheduledDate).ThenBy(p => p.MachinePmId)).ToList();
        var page = ordered.Skip((query.PageNumber - 1) * query.PageSize).Take(query.PageSize).Select(Clone).ToList();
        return Task.FromResult<(IReadOnlyList<MachinePm>, int)>((page, ordered.Count));
    }

    public Task<MachinePmBucketCountsDto> GetBucketCountsAsync(MachinePmListQuery query, DateOnly today, CancellationToken cancellationToken)
    {
        var all = Filtered(query).ToList();
        return Task.FromResult(new MachinePmBucketCountsDto
        {
            Daily = all.Count(p => InBucket(p, MachinePmBucket.Daily, today)),
            Weekly = all.Count(p => InBucket(p, MachinePmBucket.Weekly, today)),
            Monthly = all.Count(p => InBucket(p, MachinePmBucket.Monthly, today)),
            Yearly = all.Count(p => InBucket(p, MachinePmBucket.Yearly, today)),
            Completed = all.Count(p => InBucket(p, MachinePmBucket.Completed, today)),
        });
    }

    public Task<MachinePm?> GetByIdAsync(int machinePmId, CancellationToken cancellationToken)
    {
        var stored = _data.Pms.FirstOrDefault(p => p.MachinePmId == machinePmId);
        return Task.FromResult(stored is null ? null : Clone(stored));
    }

    public Task<MachinePmLookupsDto> GetLookupsAsync(CancellationToken cancellationToken) => Task.FromResult(new MachinePmLookupsDto
    {
        Machines = _data.Machines.Where(m => m.IsActive).OrderBy(m => m.MachineCode)
            .Select(m => new MachinePmMachineLookupDto { MachineId = m.MachineId, MachineCode = m.MachineCode, MachineName = m.MachineName, Location = m.Location }).ToList(),
    });

    public Task<MachinePm> CompleteAsync(MachinePm pm, byte[] originalRowVersion, MachinePmCompletionPlan plan, CancellationToken cancellationToken)
    {
        CompleteCalls++;
        BeforeWrite?.Invoke(pm.MachinePmId);
        var stored = Stored(pm.MachinePmId);

        LastLockOrder = plan.MachineIdsToLock.Append(pm.MachineId).Distinct().OrderBy(id => id).ToList();
        var locked = LastLockOrder.Select(id =>
        {
            var m = StoredMachine(id);
            return new Machine { MachineId = m.MachineId, MachineCode = m.MachineCode, OperationalStatus = m.OperationalStatus, LastMaintenanceDate = m.LastMaintenanceDate, NextMaintenanceDate = m.NextMaintenanceDate, UpdatedAt = m.UpdatedAt, UpdatedBy = m.UpdatedBy };
        }).ToList();

        if (!stored.RowVersion.SequenceEqual(originalRowVersion))
        {
            throw new ConflictException("The maintenance record was modified by another user. Refresh it and try again.");
        }

        var checklist = stored.ChecklistId is { } checklistId ? CloneChecklist(_data.Checklists.Single(c => c.ChecklistId == checklistId)) : null;
        var successor = plan.BuildSuccessor(checklist);
        if (successor is not null)
        {
            if (_data.Pms.Any(p => p != stored && IsOpen(p) && p.ChecklistId == successor.ChecklistId))
            {
                throw new ConflictException("The checklist already has an open maintenance occurrence. Refresh and try again."); // UX index
            }

            if (locked.All(m => m.MachineId != successor.MachineId))
            {
                throw new ConflictException("The maintenance record was modified by another user. Refresh it and try again.");
            }

            successor.MachinePmId = _data.Pms.Max(p => p.MachinePmId) + 1;
            successor.PmNo = $"MPM-{_data.PmSequence + 1:0000}"; // the real repository issues this from the MACHINE_PM sequence
            successor.RowVersion = new byte[] { 1 };
        }

        foreach (var machine in locked)
        {
            var openDueDates = _data.Pms.Where(p => p != stored && IsOpen(p) && p.MachineId == machine.MachineId).Select(p => p.ScheduledDate)
                .Concat(successor is not null && successor.MachineId == machine.MachineId ? new[] { successor.ScheduledDate } : Array.Empty<DateOnly>())
                .OrderBy(d => d).ToList();
            plan.ApplyToLockedMachine(machine, openDueDates);
        }

        if (FailNextSave)
        {
            FailNextSave = false;
            throw new DbUpdateException("Simulated database failure while completing.");
        }

        // "Commit": the PM, its ticks, the successor and the machines - together.
        stored.Status = pm.Status;
        stored.CompletedDate = pm.CompletedDate;
        stored.MaintenanceBy = pm.MaintenanceBy;
        stored.Remarks = pm.Remarks;
        stored.UpdatedAt = pm.UpdatedAt;
        stored.UpdatedBy = pm.UpdatedBy;
        stored.RowVersion = new[] { (byte)(stored.RowVersion[0] + 1) };
        foreach (var line in stored.ChecklistItems)
        {
            line.IsChecked = pm.ChecklistItems.Single(i => i.MachinePmChecklistId == line.MachinePmChecklistId).IsChecked;
        }

        if (successor is not null)
        {
            _data.PmSequence++;
            foreach (var line in successor.ChecklistItems)
            {
                line.MachinePmChecklistId = _nextLineId++;
                line.MachinePmId = successor.MachinePmId;
            }

            _data.Pms.Add(successor);
        }

        foreach (var copy in locked)
        {
            var machine = StoredMachine(copy.MachineId);
            machine.OperationalStatus = copy.OperationalStatus;
            machine.LastMaintenanceDate = copy.LastMaintenanceDate;
            machine.NextMaintenanceDate = copy.NextMaintenanceDate;
            machine.UpdatedAt = copy.UpdatedAt;
            machine.UpdatedBy = copy.UpdatedBy;
        }

        pm.RowVersion = stored.RowVersion;
        return Task.FromResult(pm);
    }
}
