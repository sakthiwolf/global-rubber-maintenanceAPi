using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Domain.Constants;
using GlobalRubber.MMM.Domain.Entities;
using GlobalRubber.MMM.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GlobalRubber.MMM.Infrastructure.Repositories;

public sealed class MaintenanceChecklistRepository : IMaintenanceChecklistRepository
{
    private const string ConcurrencyMessage = "The checklist was modified by another user. Refresh the checklist and try again.";

    private readonly GlobalRubberDbContext _dbContext;
    private readonly IDocumentSequenceGenerator _documentSequence;

    public MaintenanceChecklistRepository(GlobalRubberDbContext dbContext, IDocumentSequenceGenerator documentSequence)
    {
        _dbContext = dbContext;
        _documentSequence = documentSequence;
    }

    public async Task<(IReadOnlyList<MaintenanceChecklist> Items, int TotalCount)> GetAllAsync(
        MaintenanceChecklistListQuery request, CancellationToken cancellationToken)
    {
        var query = _dbContext.MaintenanceChecklists.AsNoTracking();

        if (request.IsActive is { } isActive)
        {
            query = query.Where(c => c.IsActive == isActive);
        }

        var appliesTo = request.AppliesTo?.Trim();
        if (!string.IsNullOrEmpty(appliesTo))
        {
            query = query.Where(c => c.AppliesTo == appliesTo);
        }

        if (request.MachineId is { } machineId)
        {
            query = query.Where(c => c.MachineId == machineId);
        }

        var frequency = request.Frequency?.Trim();
        if (!string.IsNullOrEmpty(frequency))
        {
            query = query.Where(c => c.Frequency == frequency);
        }

        var search = request.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            // Explicit ToLower rather than relying on the column collation being case-insensitive.
            var lowered = search.ToLower();
            query = query.Where(c => c.ChecklistCode.ToLower().Contains(lowered) || c.ChecklistName.ToLower().Contains(lowered));
        }

        var ordered = query.OrderBy(c => c.ChecklistName).ThenBy(c => c.ChecklistId);

        var totalCount = await ordered.CountAsync(cancellationToken);

        var items = await ordered
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .Include(c => c.Machine)
            .Include(c => c.Items.OrderBy(i => i.SortOrder).ThenBy(i => i.ChecklistItemId))
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public Task<MaintenanceChecklist?> GetByIdAsync(int checklistId, CancellationToken cancellationToken) =>
        _dbContext.MaintenanceChecklists
            .AsNoTracking()
            .Include(c => c.Machine)
            .Include(c => c.Items.OrderBy(i => i.SortOrder).ThenBy(i => i.ChecklistItemId))
            .FirstOrDefaultAsync(c => c.ChecklistId == checklistId, cancellationToken);

    public Task<bool> ExistsActiveByNameAsync(string name, int? excludeChecklistId, CancellationToken cancellationToken)
    {
        var lowered = name.ToLower();

        return _dbContext.MaintenanceChecklists.AsNoTracking()
            .AnyAsync(c => c.IsActive && c.ChecklistName.ToLower() == lowered && c.ChecklistId != excludeChecklistId, cancellationToken);
    }

    public async Task<MaintenanceChecklistLookupsDto> GetLookupsAsync(CancellationToken cancellationToken) => new()
    {
        Machines = await _dbContext.Machines.AsNoTracking()
            .Where(m => m.IsActive)
            .OrderBy(m => m.MachineCode)
            .Select(m => new MaintenanceChecklistMachineLookupDto { MachineId = m.MachineId, MachineCode = m.MachineCode, MachineName = m.MachineName })
            .ToListAsync(cancellationToken),
    };

    public async Task<MaintenanceChecklist> AddAsync(
        MaintenanceChecklist checklist, MachinePmOccurrencePlan? firstOccurrence, CancellationToken cancellationToken)
    {
        // The DbContext is configured with EnableRetryOnFailure, so a transaction we start ourselves must run inside
        // the execution strategy. If the caller already has a transaction open (a verification harness), join it.
        var strategy = _dbContext.Database.CreateExecutionStrategy();
        var occurrences = new MachinePmOccurrenceWriter(_dbContext, _documentSequence);
        MachinePm? occurrence = null;
        Machine? machine = null;

        try
        {
            await strategy.ExecuteAsync(async () =>
            {
                // A retry must not see the previous attempt's tracking.
                Detach(checklist);
                occurrences.Detach(occurrence);
                occurrences.Detach(machine);

                var ownsTransaction = _dbContext.Database.CurrentTransaction is null;
                await using var transaction = ownsTransaction
                    ? await _dbContext.Database.BeginTransactionAsync(cancellationToken)
                    : null;

                checklist.ChecklistCode = await _documentSequence.NextCodeAsync(DocumentTypes.MaintenanceChecklist, cancellationToken);
                _dbContext.MaintenanceChecklists.Add(checklist); // the items go in with it (checklist_id set by EF)
                await _dbContext.SaveChangesAsync(cancellationToken);

                if (firstOccurrence is not null)
                {
                    // Machine row first (see MachinePmOccurrenceWriter: one lock order everywhere), then the occurrence,
                    // then the machine's next date from ALL its open occurrences - including the one just written.
                    machine = await occurrences.LockMachineAsync(checklist.MachineId!.Value, cancellationToken);

                    occurrence = firstOccurrence.BuildOccurrence(checklist);
                    await occurrences.InsertAsync(occurrence, cancellationToken);

                    var openDueDates = await occurrences.OpenDueDatesAsync(machine.MachineId, cancellationToken);
                    firstOccurrence.ApplyToLockedMachine(machine, openDueDates);
                    await _dbContext.SaveChangesAsync(cancellationToken);
                }

                if (transaction is not null)
                {
                    await transaction.CommitAsync(cancellationToken);
                }
            });
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Nothing was committed: disposing the transaction rolled back every row and both sequence numbers.
            throw new ConflictException(ex.InnerException!.Message.Contains("UQ_machine_pm_transaction_pm_no", StringComparison.Ordinal)
                ? "The maintenance number could not be issued because it already exists. Please try again."
                : ex.InnerException.Message.Contains("UX_machine_pm_transaction_open_occurrence", StringComparison.Ordinal)
                    ? "The checklist already has an open maintenance occurrence."
                    // The checklist code is issued by the sequence, not the client, so a collision means the sequence and
                    // the table disagree.
                    : "The checklist code could not be issued because it already exists. Please try again.");
        }
        finally
        {
            // Keep the generated ids/codes/row_versions but stop tracking: a later write in the same scope attaches its
            // own stubs (and after a failure nothing half-written may linger in the change tracker).
            Detach(checklist);
            occurrences.Detach(occurrence);
            occurrences.Detach(machine);
        }

        return checklist;
    }

    public async Task<MaintenanceChecklist> UpdateAsync(
        MaintenanceChecklist checklist, byte[] originalRowVersion, bool replaceItems, ChecklistOccurrenceSyncPlan? occurrenceSync,
        CancellationToken cancellationToken)
    {
        var strategy = _dbContext.Database.CreateExecutionStrategy();
        var occurrences = new MachinePmOccurrenceWriter(_dbContext, _documentSequence);
        byte[] newRowVersion = Array.Empty<byte>();
        var lockedMachines = new List<Machine>();
        MachinePm? openOccurrence = null;
        MachinePm? newOccurrence = null;

        try
        {
            await strategy.ExecuteAsync(async () =>
            {
                // A retry must not see the previous attempt's tracking.
                lockedMachines.ForEach(occurrences.Detach);
                lockedMachines.Clear();
                occurrences.Detach(openOccurrence);
                occurrences.Detach(newOccurrence);
                openOccurrence = newOccurrence = null;

                var ownsTransaction = _dbContext.Database.CurrentTransaction is null;
                await using var transaction = ownsTransaction
                    ? await _dbContext.Database.BeginTransactionAsync(cancellationToken)
                    : null;

                await UpdateCoreAsync(transaction);
            });
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Nothing was committed. Only UX_machine_pm_transaction_open_occurrence / UQ_machine_pm_transaction_pm_no can
            // fire here (a first occurrence being inserted while another one appeared).
            throw new ConflictException(ex.InnerException!.Message.Contains("UX_machine_pm_transaction_open_occurrence", StringComparison.Ordinal)
                ? "The checklist already has an open maintenance occurrence."
                : "The maintenance number could not be issued because it already exists. Please try again.");
        }
        finally
        {
            lockedMachines.ForEach(occurrences.Detach);
            occurrences.Detach(openOccurrence);
            occurrences.Detach(newOccurrence);
        }

        checklist.RowVersion = newRowVersion;
        return checklist;

        async Task UpdateCoreAsync(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction)
        {
            if (occurrenceSync is not null)
            {
                // Lock every affected machine FIRST (one lock order everywhere: machine rows, then PM rows), in ascending
                // id order so two operations on the same pair of machines can never deadlock each other.
                var machineIds = occurrenceSync.MachineIdsToLock.ToHashSet();
                if (await occurrences.OpenOccurrenceMachineIdAsync(checklist.ChecklistId, cancellationToken) is { } openMachineId)
                {
                    machineIds.Add(openMachineId);
                }

                foreach (var machineId in machineIds.OrderBy(id => id))
                {
                    lockedMachines.Add(await occurrences.LockMachineAsync(machineId, cancellationToken));
                }
            }

            // Header first. A stub (key + the caller's row version + only the values this operation writes): the row
            // version the CALLER holds becomes the ORIGINAL value in the UPDATE's WHERE clause. The header UPDATE always
            // runs (updated_at changes), so the header row_version moves on every edit - including item-only edits -
            // and a second editor holding the old version is refused.
            var stub = new MaintenanceChecklist
            {
                ChecklistId = checklist.ChecklistId,
                RowVersion = originalRowVersion,
                ChecklistName = checklist.ChecklistName,
                AppliesTo = checklist.AppliesTo,
                Frequency = checklist.Frequency,
                MachineId = checklist.MachineId,
                StartDate = checklist.StartDate,
                UpdatedAt = checklist.UpdatedAt,
                UpdatedBy = checklist.UpdatedBy,
            };

            var entry = _dbContext.Attach(stub);
            entry.Property(c => c.ChecklistName).IsModified = true;
            entry.Property(c => c.AppliesTo).IsModified = true;
            entry.Property(c => c.Frequency).IsModified = true;
            entry.Property(c => c.MachineId).IsModified = true;
            entry.Property(c => c.StartDate).IsModified = true;
            entry.Property(c => c.UpdatedAt).IsModified = true;
            entry.Property(c => c.UpdatedBy).IsModified = true;

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Nothing was written; disposing the transaction rolls it back.
                throw new ConflictException(ConcurrencyMessage);
            }
            finally
            {
                entry.State = EntityState.Detached;
            }

            newRowVersion = stub.RowVersion; // refreshed by SQL Server on save

            if (replaceItems)
            {
                // Items are replaced as a set (analysis 13.x). PM checklist rows keep their own label snapshot and their
                // checklist_item_id is set to NULL by FK ..._item ON DELETE SET NULL, so history is unaffected.
                await _dbContext.MaintenanceChecklistItems
                    .Where(i => i.ChecklistId == checklist.ChecklistId)
                    .ExecuteDeleteAsync(cancellationToken);

                foreach (var item in checklist.Items)
                {
                    item.ChecklistItemId = 0;
                    item.ChecklistId = checklist.ChecklistId;
                    _dbContext.MaintenanceChecklistItems.Add(item);
                }

                try
                {
                    await _dbContext.SaveChangesAsync(cancellationToken);
                }
                finally
                {
                    foreach (var item in checklist.Items)
                    {
                        _dbContext.Entry(item).State = EntityState.Detached;
                    }
                }
            }

            if (occurrenceSync is not null)
            {
                // The open occurrence, read AFTER the machine locks are held. If it is not on a locked machine it was moved
                // by someone else in between: refuse rather than write around their change.
                openOccurrence = await occurrences.LoadOpenOccurrenceAsync(checklist.ChecklistId, cancellationToken);
                EnsureLocked(openOccurrence?.MachineId);

                var change = occurrenceSync.Decide(checklist, openOccurrence); // may re-date / move the open occurrence in place
                EnsureLocked(openOccurrence?.MachineId);

                if (change.ReplacementSnapshot is not null && openOccurrence is not null)
                {
                    occurrences.ReplaceSnapshot(openOccurrence, change.ReplacementSnapshot);
                }

                await _dbContext.SaveChangesAsync(cancellationToken);

                if (change.NewOccurrence is not null)
                {
                    newOccurrence = change.NewOccurrence;
                    EnsureLocked(newOccurrence.MachineId);
                    await occurrences.InsertAsync(newOccurrence, cancellationToken); // MACHINE_PM number, snapshot
                }

                // Every machine whose open occurrences may have changed (old and new): next date = earliest open one.
                foreach (var machine in lockedMachines)
                {
                    occurrenceSync.ApplyToLockedMachine(machine, await occurrences.OpenDueDatesAsync(machine.MachineId, cancellationToken));
                }

                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
        }

        void EnsureLocked(int? machineId)
        {
            if (machineId is { } id && lockedMachines.All(m => m.MachineId != id))
            {
                throw new ConflictException(ConcurrencyMessage);
            }
        }
    }

    public async Task<MaintenanceChecklist> DeactivateAsync(MaintenanceChecklist checklist, CancellationToken cancellationToken)
    {
        // Stub carrying the row version the checklist was loaded with; the ONLY columns marked modified are is_active,
        // updated_at and updated_by - the UPDATE cannot touch anything else, the items are untouched, and no DELETE is
        // ever issued.
        var stub = new MaintenanceChecklist
        {
            ChecklistId = checklist.ChecklistId,
            RowVersion = checklist.RowVersion,
            IsActive = checklist.IsActive,
            UpdatedAt = checklist.UpdatedAt,
            UpdatedBy = checklist.UpdatedBy,
        };

        var entry = _dbContext.Attach(stub);
        entry.Property(c => c.IsActive).IsModified = true;
        entry.Property(c => c.UpdatedAt).IsModified = true;
        entry.Property(c => c.UpdatedBy).IsModified = true;

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            entry.State = EntityState.Detached;
            throw new ConflictException(ConcurrencyMessage);
        }

        checklist.RowVersion = stub.RowVersion;
        entry.State = EntityState.Detached;
        return checklist;
    }

    private void Detach(MaintenanceChecklist checklist)
    {
        _dbContext.Entry(checklist).State = EntityState.Detached;
        foreach (var item in checklist.Items)
        {
            _dbContext.Entry(item).State = EntityState.Detached;
        }
    }

    // 2601 = unique index violation, 2627 = unique/primary key constraint violation.
    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: 2601 or 2627 };
}
