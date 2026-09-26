using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Domain.Constants;
using GlobalRubber.MMM.Domain.Entities;

namespace GlobalRubber.MMM.Tests;

/// <summary>
/// Shared test data and in-memory fake for Machine Breakdown tests.
///
/// Machines are the existing MachineTestData (1: MAC-0001 active, 2: MAC-0002 active, 3: MAC-0003 inactive).
///
/// Breakdowns seeded here so far:
///   1 BRK-0001 machineId=1, Reported
///   2 BRK-0002 machineId=2, Assigned
/// </summary>
internal static class MachineBreakdownTestData
{
    public static List<MachineBreakdown> Breakdowns() => new()
    {
        Breakdown(1, "BRK-0001", 1, BreakdownStage.Reported),
        Breakdown(2, "BRK-0002", 2, BreakdownStage.Assigned),
    };

    private static MachineBreakdown Breakdown(int id, string no, int machineId, string stage) => new()
    {
        MachineBreakdownId = id,
        BreakdownNo = no,
        MachineId = machineId,
        BreakdownDate = new DateOnly(2026, 9, 1),
        BreakdownTime = new TimeOnly(8, 0),
        ReportedBy = "Test Reporter",
        Problem = $"Problem for {no}",
        Priority = BreakdownPriority.Medium,
        Stage = stage,
        CreatedAt = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc),
        CreatedBy = 1,
        RowVersion = new byte[] { 0x01 },
    };
}

/// <summary>
/// In-memory fake for IMachineBreakdownRepository.
/// AddAsync assigns the next BRK-NNNN number. UpdateStageAsync respects optimistic concurrency.
/// </summary>
internal sealed class InMemoryMachineBreakdownRepository : IMachineBreakdownRepository
{
    private readonly List<MachineBreakdown> _breakdowns;
    private readonly List<Machine> _machines;
    private int _lastNumber;

    public InMemoryMachineBreakdownRepository(List<MachineBreakdown> breakdowns, List<Machine> machines)
    {
        _breakdowns = breakdowns;
        _machines = machines;
        _lastNumber = breakdowns.Count;
    }

    public bool FailNextSave { get; set; }
    public int Count => _breakdowns.Count;
    public MachineBreakdown Stored(int id) => _breakdowns.Single(b => b.MachineBreakdownId == id);

    public Task<(IReadOnlyList<MachineBreakdown> Items, int TotalCount)> GetAllAsync(
        MachineBreakdownListQuery query, CancellationToken cancellationToken)
    {
        IEnumerable<MachineBreakdown> q = _breakdowns;
        if (query.MachineId is { } mid) q = q.Where(b => b.MachineId == mid);
        if (!string.IsNullOrEmpty(query.Stage?.Trim())) q = q.Where(b => b.Stage == query.Stage.Trim());
        var search = query.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            var lo = search.ToLower();
            q = q.Where(b =>
                b.BreakdownNo.ToLower().Contains(lo) ||
                b.Problem.ToLower().Contains(lo) ||
                (b.ReportedBy != null && b.ReportedBy.ToLower().Contains(lo)));
        }
        var all = q.OrderByDescending(b => b.MachineBreakdownId).ToList();
        var page = all.Skip((query.PageNumber - 1) * query.PageSize).Take(query.PageSize)
            .Select(b => Clone(b)).ToList();
        return Task.FromResult<(IReadOnlyList<MachineBreakdown>, int)>((page, all.Count));
    }

    public Task<MachineBreakdown?> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        var stored = _breakdowns.FirstOrDefault(b => b.MachineBreakdownId == id);
        return Task.FromResult(stored is null ? (MachineBreakdown?)null : Clone(stored));
    }

    public Task<MachineBreakdown> AddAsync(MachineBreakdown breakdown, CancellationToken cancellationToken)
    {
        if (FailNextSave)
        {
            FailNextSave = false;
            throw new ConflictException("Simulated save failure.");
        }

        _lastNumber++;
        breakdown.MachineBreakdownId = _breakdowns.Count + 1;
        breakdown.BreakdownNo = $"BRK-{_lastNumber:0000}";
        breakdown.RowVersion = new byte[] { 0x01 };
        var machine = _machines.SingleOrDefault(m => m.MachineId == breakdown.MachineId);
        var cloned = Clone(breakdown);
        cloned.Machine = machine!;
        _breakdowns.Add(Clone(breakdown));
        return Task.FromResult(cloned);
    }

    public Task<MachineBreakdown> UpdateStageAsync(
        MachineBreakdown breakdown, byte[] originalRowVersion, CancellationToken cancellationToken)
    {
        var stored = _breakdowns.FirstOrDefault(b => b.MachineBreakdownId == breakdown.MachineBreakdownId)
            ?? throw new NotFoundException(nameof(MachineBreakdown), breakdown.MachineBreakdownId);

        if (!stored.RowVersion.SequenceEqual(originalRowVersion))
        {
            throw new ConflictException("The breakdown record was modified by another user. Refresh it and try again.");
        }

        stored.Stage = breakdown.Stage;
        stored.AssignedEngineerId = breakdown.AssignedEngineerId;
        stored.AssignedAt = breakdown.AssignedAt;
        stored.MaintenanceStartedAt = breakdown.MaintenanceStartedAt;
        stored.ResolvedAt = breakdown.ResolvedAt;
        stored.ClosedAt = breakdown.ClosedAt;
        stored.RootCause = breakdown.RootCause;
        stored.CorrectiveAction = breakdown.CorrectiveAction;
        stored.DowntimeHours = breakdown.DowntimeHours;
        stored.UpdatedAt = breakdown.UpdatedAt;
        stored.UpdatedBy = breakdown.UpdatedBy;
        // Bump fake row version.
        stored.RowVersion = new byte[] { (byte)(stored.RowVersion[0] + 1) };
        breakdown.RowVersion = stored.RowVersion;

        var updated = Clone(stored);
        updated.Machine = _machines.SingleOrDefault(m => m.MachineId == stored.MachineId)!;
        return Task.FromResult(updated);
    }

    private static MachineBreakdown Clone(MachineBreakdown b) => new()
    {
        MachineBreakdownId = b.MachineBreakdownId,
        BreakdownNo = b.BreakdownNo,
        MachineId = b.MachineId,
        BreakdownDate = b.BreakdownDate,
        BreakdownTime = b.BreakdownTime,
        ReportedBy = b.ReportedBy,
        Problem = b.Problem,
        BreakdownTypeId = b.BreakdownTypeId,
        Priority = b.Priority,
        Description = b.Description,
        Stage = b.Stage,
        AssignedEngineerId = b.AssignedEngineerId,
        AssignedAt = b.AssignedAt,
        MaintenanceStartedAt = b.MaintenanceStartedAt,
        ResolvedAt = b.ResolvedAt,
        ClosedAt = b.ClosedAt,
        RootCause = b.RootCause,
        CorrectiveAction = b.CorrectiveAction,
        DowntimeHours = b.DowntimeHours,
        CreatedAt = b.CreatedAt,
        CreatedBy = b.CreatedBy,
        UpdatedAt = b.UpdatedAt,
        UpdatedBy = b.UpdatedBy,
        RowVersion = (byte[])b.RowVersion.Clone(),
        Machine = b.Machine,
    };
}
