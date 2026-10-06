using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Domain.Constants;
using GlobalRubber.MMM.Domain.Entities;
using GlobalRubber.MMM.Domain.Rules;
using GlobalRubber.MMM.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GlobalRubber.MMM.Infrastructure.Repositories;

public sealed class MachineBreakdownRepository : IMachineBreakdownRepository
{
    private const string ConcurrencyMessage =
        "The breakdown record was modified by another user. Refresh it and try again.";

    private readonly GlobalRubberDbContext _dbContext;
    private readonly IDocumentSequenceGenerator _documentSequence;

    public MachineBreakdownRepository(GlobalRubberDbContext dbContext, IDocumentSequenceGenerator documentSequence)
    {
        _dbContext = dbContext;
        _documentSequence = documentSequence;
    }

    public async Task<(IReadOnlyList<MachineBreakdown> Items, int TotalCount)> GetAllAsync(
        MachineBreakdownListQuery query, CancellationToken cancellationToken)
    {
        var q = _dbContext.MachineBreakdowns.AsNoTracking();

        if (query.MachineId is { } machineId)
        {
            q = q.Where(b => b.MachineId == machineId);
        }

        if (query.ActiveOnly == true)
        {
            q = q.Where(b => b.Stage != BreakdownStage.Closed);
        }

        if (!string.IsNullOrEmpty(query.Stage?.Trim()))
        {
            var stage = query.Stage.Trim();
            q = q.Where(b => b.Stage == stage);
        }

        var search = query.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            var lowered = search.ToLower();
            q = q.Where(b =>
                b.BreakdownNo.ToLower().Contains(lowered) ||
                b.Machine.MachineCode.ToLower().Contains(lowered) ||
                b.Machine.MachineName.ToLower().Contains(lowered) ||
                b.Problem.ToLower().Contains(lowered) ||
                (b.ReportedBy != null && b.ReportedBy.ToLower().Contains(lowered)));
        }

        // Newest first.
        var ordered = q.OrderByDescending(b => b.MachineBreakdownId);
        var totalCount = await ordered.CountAsync(cancellationToken);

        var items = await ordered
            .Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize)
            .Include(b => b.Machine)
            .Include(b => b.BreakdownType)
            .Include(b => b.AssignedEngineer)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public Task<MachineBreakdown?> GetByIdAsync(int machineBreakdownId, CancellationToken cancellationToken) =>
        _dbContext.MachineBreakdowns
            .AsNoTracking()
            .Include(b => b.Machine)
            .Include(b => b.BreakdownType)
            .Include(b => b.AssignedEngineer)
            .FirstOrDefaultAsync(b => b.MachineBreakdownId == machineBreakdownId, cancellationToken);

    public async Task<MachineBreakdown> AddAsync(MachineBreakdown breakdown, BreakdownMachineStatusPlan? machinePlan, CancellationToken cancellationToken)
    {
        var strategy = _dbContext.Database.CreateExecutionStrategy();
        var machineLock = new MachinePmOccurrenceWriter(_dbContext, _documentSequence); // the machine-row lock Machine PM uses
        Machine? machine = null;

        try
        {
            await strategy.ExecuteAsync(async () =>
            {
                Detach(breakdown);
                machineLock.Detach(machine);

                var ownsTransaction = _dbContext.Database.CurrentTransaction is null;
                await using var transaction = ownsTransaction
                    ? await _dbContext.Database.BeginTransactionAsync(cancellationToken)
                    : null;

                // Machine row first (the lock order Machine PM uses everywhere), then the sequence and the insert.
                if (machinePlan is not null)
                {
                    machine = await machineLock.LockMachineAsync(breakdown.MachineId, cancellationToken);
                    machinePlan.ApplyToLockedMachine(machine, await CountOtherUnresolvedAsync(breakdown.MachineId, excludeId: null, cancellationToken));
                }

                breakdown.BreakdownNo = await _documentSequence.NextCodeAsync(
                    DocumentTypes.MachineBreakdown, cancellationToken);

                _dbContext.MachineBreakdowns.Add(breakdown);
                await _dbContext.SaveChangesAsync(cancellationToken); // the breakdown and the machine change together

                if (transaction is not null)
                {
                    await transaction.CommitAsync(cancellationToken);
                }
            });
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            Detach(breakdown);
            machineLock.Detach(machine);
            throw new ConflictException(
                "The breakdown number could not be issued because it already exists. Please try again.");
        }
        catch
        {
            Detach(breakdown);
            machineLock.Detach(machine);
            throw;
        }

        Detach(breakdown);
        machineLock.Detach(machine);
        return breakdown;
    }

    public async Task<MachineBreakdown> UpdateStageAsync(
        MachineBreakdown breakdown, byte[] originalRowVersion, BreakdownMachineStatusPlan? machinePlan, CancellationToken cancellationToken)
    {
        var strategy = _dbContext.Database.CreateExecutionStrategy();
        var machineLock = new MachinePmOccurrenceWriter(_dbContext, _documentSequence);
        Machine? machine = null;

        try
        {
            await strategy.ExecuteAsync(async () =>
            {
                DetachById(breakdown.MachineBreakdownId);
                machineLock.Detach(machine);

                var ownsTransaction = _dbContext.Database.CurrentTransaction is null;
                await using var transaction = ownsTransaction
                    ? await _dbContext.Database.BeginTransactionAsync(cancellationToken)
                    : null;

                // Machine row first (same lock order as create and Machine PM); the other breakdowns are counted while
                // the machine is locked, so two resolutions of the same machine cannot both miss each other.
                if (machinePlan is not null)
                {
                    machine = await machineLock.LockMachineAsync(breakdown.MachineId, cancellationToken);
                    machinePlan.ApplyToLockedMachine(machine, await CountOtherUnresolvedAsync(breakdown.MachineId, breakdown.MachineBreakdownId, cancellationToken));
                }

                var entry = _dbContext.Attach(new MachineBreakdown { MachineBreakdownId = breakdown.MachineBreakdownId, RowVersion = originalRowVersion });
                // Only write the columns that a stage advance can change.
                entry.Property(b => b.Stage).IsModified = true;
                entry.Property(b => b.AssignedEngineerId).IsModified = true;
                entry.Property(b => b.AssignedToName).IsModified = true;
                entry.Property(b => b.AssignedAt).IsModified = true;
                entry.Property(b => b.MaintenanceStartedAt).IsModified = true;
                entry.Property(b => b.ResolvedAt).IsModified = true;
                entry.Property(b => b.ClosedAt).IsModified = true;
                entry.Property(b => b.RootCause).IsModified = true;
                entry.Property(b => b.CorrectiveAction).IsModified = true;
                entry.Property(b => b.DowntimeHours).IsModified = true;
                entry.Property(b => b.UpdatedAt).IsModified = true;
                entry.Property(b => b.UpdatedBy).IsModified = true;

                // Copy values from the caller's breakdown.
                entry.Entity.Stage = breakdown.Stage;
                entry.Entity.AssignedEngineerId = breakdown.AssignedEngineerId;
                entry.Entity.AssignedToName = breakdown.AssignedToName;
                entry.Entity.AssignedAt = breakdown.AssignedAt;
                entry.Entity.MaintenanceStartedAt = breakdown.MaintenanceStartedAt;
                entry.Entity.ResolvedAt = breakdown.ResolvedAt;
                entry.Entity.ClosedAt = breakdown.ClosedAt;
                entry.Entity.RootCause = breakdown.RootCause;
                entry.Entity.CorrectiveAction = breakdown.CorrectiveAction;
                entry.Entity.DowntimeHours = breakdown.DowntimeHours;
                entry.Entity.UpdatedAt = breakdown.UpdatedAt;
                entry.Entity.UpdatedBy = breakdown.UpdatedBy;

                try
                {
                    await _dbContext.SaveChangesAsync(cancellationToken);
                }
                catch (DbUpdateConcurrencyException)
                {
                    throw new ConflictException(ConcurrencyMessage);
                }

                breakdown.RowVersion = entry.Entity.RowVersion;

                if (transaction is not null)
                {
                    await transaction.CommitAsync(cancellationToken);
                }
            });
        }
        finally
        {
            DetachById(breakdown.MachineBreakdownId);
            machineLock.Detach(machine);
        }

        return breakdown;
    }

    // The machine's OTHER breakdowns that still keep it down (before Resolved).
    private Task<int> CountOtherUnresolvedAsync(int machineId, int? excludeId, CancellationToken cancellationToken) =>
        _dbContext.MachineBreakdowns.AsNoTracking()
            .Where(b => b.MachineId == machineId && b.MachineBreakdownId != excludeId && MachineBreakdownRules.UnresolvedStages.Contains(b.Stage))
            .CountAsync(cancellationToken);

    private void Detach(MachineBreakdown breakdown)
    {
        _dbContext.Entry(breakdown).State = EntityState.Detached;
    }

    private void DetachById(int id)
    {
        var tracked = _dbContext.ChangeTracker.Entries<MachineBreakdown>()
            .Where(e => e.Entity.MachineBreakdownId == id)
            .ToList();
        foreach (var e in tracked)
        {
            e.State = EntityState.Detached;
        }
    }

    // 2601 = unique index violation, 2627 = unique/primary key constraint violation.
    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: 2601 or 2627 };
}
