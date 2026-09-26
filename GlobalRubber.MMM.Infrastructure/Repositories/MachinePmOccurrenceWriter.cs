using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Domain.Constants;
using GlobalRubber.MMM.Domain.Entities;
using GlobalRubber.MMM.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GlobalRubber.MMM.Infrastructure.Repositories;

/// <summary>
/// The write steps of a recurring Machine PM occurrence, used INSIDE a caller's transaction (it never begins or commits
/// one). Lock order, everywhere: the machine row first, then its PM rows - so two operations on the same machine queue
/// on the machine instead of deadlocking on each other's PM rows.
/// </summary>
internal sealed class MachinePmOccurrenceWriter
{
    private readonly GlobalRubberDbContext _dbContext;
    private readonly IDocumentSequenceGenerator _documentSequence;

    public MachinePmOccurrenceWriter(GlobalRubberDbContext dbContext, IDocumentSequenceGenerator documentSequence)
    {
        _dbContext = dbContext;
        _documentSequence = documentSequence;
    }

    /// <summary>Reads the machine row with an update lock held until the transaction ends (tracked, so changes are saved).</summary>
    public async Task<Machine> LockMachineAsync(int machineId, CancellationToken cancellationToken)
    {
        // A tracked copy would be returned as-is (stale) instead of the locked row - make sure there is none.
        foreach (var tracked in _dbContext.ChangeTracker.Entries<Machine>().Where(e => e.Entity.MachineId == machineId).ToList())
        {
            tracked.State = EntityState.Detached;
        }

        return await _dbContext.Machines
            .FromSqlInterpolated($"SELECT * FROM masters.machine_master WITH (UPDLOCK, ROWLOCK) WHERE machine_id = {machineId}")
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException(nameof(Machine), machineId);
    }

    /// <summary>Issues the PM number from the MACHINE_PM sequence and inserts the occurrence with its checklist snapshot.</summary>
    public async Task InsertAsync(MachinePm occurrence, CancellationToken cancellationToken)
    {
        occurrence.PmNo = await _documentSequence.NextCodeAsync(DocumentTypes.MachinePm, cancellationToken);
        _dbContext.MachinePms.Add(occurrence); // the snapshot lines go in with it
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Due dates of the machine's open occurrences (Scheduled / In Progress), as seen inside the transaction.</summary>
    public async Task<IReadOnlyList<DateOnly>> OpenDueDatesAsync(int machineId, CancellationToken cancellationToken) =>
        await _dbContext.MachinePms.AsNoTracking()
            .Where(p => p.MachineId == machineId && (p.Status == MachinePmStatus.Scheduled || p.Status == MachinePmStatus.InProgress))
            .OrderBy(p => p.ScheduledDate)
            .Select(p => p.ScheduledDate)
            .ToListAsync(cancellationToken);

    /// <summary>The machine the checklist's open occurrence is on (read without locks - only to know what to lock).</summary>
    public Task<int?> OpenOccurrenceMachineIdAsync(int checklistId, CancellationToken cancellationToken) =>
        _dbContext.MachinePms.AsNoTracking()
            .Where(p => p.ChecklistId == checklistId && (p.Status == MachinePmStatus.Scheduled || p.Status == MachinePmStatus.InProgress))
            .Select(p => (int?)p.MachineId)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>
    /// The checklist's open occurrence (at most one - UX_machine_pm_transaction_open_occurrence), tracked with its snapshot
    /// lines. Call after the machine locks are held.
    /// </summary>
    public Task<MachinePm?> LoadOpenOccurrenceAsync(int checklistId, CancellationToken cancellationToken) =>
        _dbContext.MachinePms
            .Include(p => p.ChecklistItems)
            .SingleOrDefaultAsync(p => p.ChecklistId == checklistId && (p.Status == MachinePmStatus.Scheduled || p.Status == MachinePmStatus.InProgress), cancellationToken);

    /// <summary>Removes the OPEN occurrence's snapshot lines and adds the replacement lines (saved by the caller).</summary>
    public void ReplaceSnapshot(MachinePm openOccurrence, IReadOnlyList<MachinePmChecklistItem> lines)
    {
        _dbContext.MachinePmChecklistItems.RemoveRange(openOccurrence.ChecklistItems.ToList());
        foreach (var line in lines)
        {
            line.MachinePmChecklistId = 0;
            line.MachinePmId = openOccurrence.MachinePmId;
            _dbContext.MachinePmChecklistItems.Add(line);
        }
    }

    public void Detach(MachinePm? occurrence)
    {
        if (occurrence is null) return;
        _dbContext.Entry(occurrence).State = EntityState.Detached;
        foreach (var line in occurrence.ChecklistItems)
        {
            _dbContext.Entry(line).State = EntityState.Detached;
        }
    }

    public void Detach(Machine? machine)
    {
        if (machine is not null) _dbContext.Entry(machine).State = EntityState.Detached;
    }
}
