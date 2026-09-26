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

public sealed class MachinePmRepository : IMachinePmRepository
{
    private const string ConcurrencyMessage = "The maintenance record was modified by another user. Refresh it and try again.";

    private readonly GlobalRubberDbContext _dbContext;
    private readonly IDocumentSequenceGenerator _documentSequence;

    public MachinePmRepository(GlobalRubberDbContext dbContext, IDocumentSequenceGenerator documentSequence)
    {
        _dbContext = dbContext;
        _documentSequence = documentSequence;
    }

    public async Task<(IReadOnlyList<MachinePm> Items, int TotalCount)> GetAllAsync(
        MachinePmListQuery request, DateOnly today, CancellationToken cancellationToken)
    {
        var query = ApplyBucket(Filtered(request), request.Bucket, today);

        // Open work in date order (what is due first); completed work newest first.
        var ordered = request.Bucket == MachinePmBucket.Completed
            ? query.OrderByDescending(p => p.CompletedDate).ThenByDescending(p => p.MachinePmId)
            : query.OrderBy(p => p.ScheduledDate).ThenBy(p => p.MachinePmId);

        var totalCount = await ordered.CountAsync(cancellationToken);

        var items = await ordered
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .Include(p => p.Machine)
            .Include(p => p.MaintenanceType)
            .Include(p => p.Checklist)
            .Include(p => p.ChecklistItems.OrderBy(i => i.SortOrder).ThenBy(i => i.MachinePmChecklistId))
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<MachinePmBucketCountsDto> GetBucketCountsAsync(MachinePmListQuery request, DateOnly today, CancellationToken cancellationToken)
    {
        // Same rule as ApplyBucket: only DUE open work (scheduled on or before today) counts in a frequency tab.
        var counts = await Filtered(request)
            .GroupBy(_ => 1)
            .Select(g => new MachinePmBucketCountsDto
            {
                Daily = g.Count(p => p.Status != MachinePmStatus.Completed && p.ScheduledDate <= today && p.Checklist!.Frequency == ChecklistFrequency.Daily),
                Weekly = g.Count(p => p.Status != MachinePmStatus.Completed && p.ScheduledDate <= today && p.Checklist!.Frequency == ChecklistFrequency.Weekly),
                Monthly = g.Count(p => p.Status != MachinePmStatus.Completed && p.ScheduledDate <= today && p.Checklist!.Frequency == ChecklistFrequency.Monthly),
                Yearly = g.Count(p => p.Status != MachinePmStatus.Completed && p.ScheduledDate <= today && p.Checklist!.Frequency == ChecklistFrequency.Yearly),
                Completed = g.Count(p => p.Status == MachinePmStatus.Completed),
            })
            .FirstOrDefaultAsync(cancellationToken);

        return counts ?? new MachinePmBucketCountsDto();
    }

    public Task<MachinePm?> GetByIdAsync(int machinePmId, CancellationToken cancellationToken) =>
        _dbContext.MachinePms
            .AsNoTracking()
            .Include(p => p.Machine)
            .Include(p => p.MaintenanceType)
            .Include(p => p.Checklist)
            .Include(p => p.ChecklistItems.OrderBy(i => i.SortOrder).ThenBy(i => i.MachinePmChecklistId))
            .FirstOrDefaultAsync(p => p.MachinePmId == machinePmId, cancellationToken);

    public async Task<MachinePmLookupsDto> GetLookupsAsync(CancellationToken cancellationToken) => new()
    {
        Machines = await _dbContext.Machines.AsNoTracking()
            .Where(m => m.IsActive)
            .OrderBy(m => m.MachineCode)
            .Select(m => new MachinePmMachineLookupDto { MachineId = m.MachineId, MachineCode = m.MachineCode, MachineName = m.MachineName, Location = m.Location })
            .ToListAsync(cancellationToken),
    };

    public async Task<MachinePm> CompleteAsync(MachinePm pm, byte[] originalRowVersion, MachinePmCompletionPlan plan, CancellationToken cancellationToken)
    {
        // The DbContext is configured with EnableRetryOnFailure, so a transaction we start ourselves must run inside
        // the execution strategy. If the caller already has a transaction open (a verification harness), join it.
        var strategy = _dbContext.Database.CreateExecutionStrategy();
        var occurrences = new MachinePmOccurrenceWriter(_dbContext, _documentSequence);
        var lockedMachines = new List<Machine>();
        MachinePm? header = null;
        List<MachinePmChecklistItem> lines = new();
        MachinePm? successor = null;
        byte[] newRowVersion = Array.Empty<byte>();

        try
        {
            await strategy.ExecuteAsync(async () =>
            {
                DetachAll(); // a retry must not see the previous attempt's tracking

                var ownsTransaction = _dbContext.Database.CurrentTransaction is null;
                await using var transaction = ownsTransaction
                    ? await _dbContext.Database.BeginTransactionAsync(cancellationToken)
                    : null;

                // 1. Lock every affected machine first (one lock order everywhere: machine rows, then PM rows), ascending.
                foreach (var machineId in plan.MachineIdsToLock.Append(pm.MachineId).Distinct().OrderBy(id => id))
                {
                    lockedMachines.Add(await occurrences.LockMachineAsync(machineId, cancellationToken));
                }

                // 2. Complete the PM. The caller's row version is the ORIGINAL value in the UPDATE's WHERE clause, and only
                //    the completion columns are written - scheduled date, machine, checklist and number never change.
                header = new MachinePm
                {
                    MachinePmId = pm.MachinePmId,
                    RowVersion = originalRowVersion,
                    Status = pm.Status,
                    CompletedDate = pm.CompletedDate,
                    Remarks = pm.Remarks,
                    MaintenanceBy = pm.MaintenanceBy,
                    UpdatedAt = pm.UpdatedAt,
                    UpdatedBy = pm.UpdatedBy,
                };
                var headerEntry = _dbContext.Attach(header);
                headerEntry.Property(p => p.Status).IsModified = true;
                headerEntry.Property(p => p.CompletedDate).IsModified = true;
                headerEntry.Property(p => p.Remarks).IsModified = true;
                headerEntry.Property(p => p.MaintenanceBy).IsModified = true;
                headerEntry.Property(p => p.UpdatedAt).IsModified = true;
                headerEntry.Property(p => p.UpdatedBy).IsModified = true;

                lines = pm.ChecklistItems
                    .Select(i => new MachinePmChecklistItem { MachinePmChecklistId = i.MachinePmChecklistId, MachinePmId = pm.MachinePmId, IsChecked = i.IsChecked })
                    .ToList();
                foreach (var line in lines)
                {
                    _dbContext.Attach(line).Property(l => l.IsChecked).IsModified = true;
                }

                try
                {
                    await _dbContext.SaveChangesAsync(cancellationToken);
                }
                catch (DbUpdateConcurrencyException)
                {
                    throw new ConflictException(ConcurrencyMessage); // nothing committed: the transaction rolls back
                }

                newRowVersion = header.RowVersion; // refreshed by SQL Server on save

                // 3. The successor, from the checklist as it is NOW (its current items), if the plan says one is due.
                var checklist = pm.ChecklistId is { } checklistId
                    ? await _dbContext.MaintenanceChecklists.AsNoTracking()
                        .Include(c => c.Items.OrderBy(i => i.SortOrder).ThenBy(i => i.ChecklistItemId))
                        .FirstOrDefaultAsync(c => c.ChecklistId == checklistId, cancellationToken)
                    : null;

                successor = plan.BuildSuccessor(checklist);
                if (successor is not null)
                {
                    if (lockedMachines.All(m => m.MachineId != successor.MachineId))
                    {
                        throw new ConflictException(ConcurrencyMessage); // the checklist moved machine after it was read
                    }

                    await occurrences.InsertAsync(successor, cancellationToken); // MACHINE_PM number + snapshot
                }

                // 4. Machine dates: last maintenance (the PM's machine) and next = earliest open occurrence (every machine).
                foreach (var machine in lockedMachines)
                {
                    plan.ApplyToLockedMachine(machine, await occurrences.OpenDueDatesAsync(machine.MachineId, cancellationToken));
                }

                await _dbContext.SaveChangesAsync(cancellationToken);

                if (transaction is not null)
                {
                    await transaction.CommitAsync(cancellationToken);
                }
            });
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Only UX_machine_pm_transaction_open_occurrence / UQ_machine_pm_transaction_pm_no can fire: a second open
            // occurrence for the checklist, or the sequence and the table disagree. Nothing was committed.
            throw new ConflictException(ex.InnerException!.Message.Contains("UX_machine_pm_transaction_open_occurrence", StringComparison.Ordinal)
                ? "The checklist already has an open maintenance occurrence. Refresh and try again."
                : "The maintenance number could not be issued because it already exists. Please try again.");
        }
        finally
        {
            DetachAll();
        }

        pm.RowVersion = newRowVersion;
        return pm;

        void DetachAll()
        {
            if (header is not null) _dbContext.Entry(header).State = EntityState.Detached;
            foreach (var line in lines) _dbContext.Entry(line).State = EntityState.Detached;
            occurrences.Detach(successor);
            lockedMachines.ForEach(occurrences.Detach);
            lockedMachines.Clear();
        }
    }

    private IQueryable<MachinePm> Filtered(MachinePmListQuery request)
    {
        var query = _dbContext.MachinePms.AsNoTracking();

        if (request.MachineId is { } machineId)
        {
            query = query.Where(p => p.MachineId == machineId);
        }

        var search = request.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            // Explicit ToLower rather than relying on the column collation being case-insensitive.
            var lowered = search.ToLower();
            query = query.Where(p => p.PmNo.ToLower().Contains(lowered)
                                     || p.Machine.MachineCode.ToLower().Contains(lowered)
                                     || p.Machine.MachineName.ToLower().Contains(lowered));
        }

        return query;
    }

    // The tabs: DUE open PMs by their checklist's FREQUENCY - scheduled on or before today (IST), so a successor created at
    // completion for a future cycle date stays hidden until that date; overdue ones stay in their frequency tab - and
    // completed PMs together.
    private static IQueryable<MachinePm> ApplyBucket(IQueryable<MachinePm> query, string? bucket, DateOnly today)
    {
        if (bucket == MachinePmBucket.Completed)
        {
            return query.Where(p => p.Status == MachinePmStatus.Completed);
        }

        var frequency = bucket is null ? null : MachinePmBucket.FrequencyOf(bucket);
        return frequency is null
            ? query
            : query.Where(p => p.Status != MachinePmStatus.Completed && p.ScheduledDate <= today && p.Checklist!.Frequency == frequency);
    }

    // 2601 = unique index violation, 2627 = unique/primary key constraint violation.
    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: 2601 or 2627 };
}
