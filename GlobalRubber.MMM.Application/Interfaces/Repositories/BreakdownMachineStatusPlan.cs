using GlobalRubber.MMM.Domain.Entities;

namespace GlobalRubber.MMM.Application.Interfaces.Repositories;

/// <summary>
/// What a breakdown write does to its machine, applied by the repository INSIDE the same transaction: the machine row is
/// locked (UPDLOCK, the same lock Machine PM uses) before the breakdown row is written, then
/// <see cref="ApplyToLockedMachine"/> receives the tracked machine and the number of the machine's OTHER breakdowns that are
/// still unresolved (Reported / Assigned / Maintenance Started). Whatever the callback changes on the machine is saved
/// with the breakdown, and a failure rolls both back. The business rule itself stays in the service.
/// </summary>
public sealed class BreakdownMachineStatusPlan
{
    public required Action<Machine, int> ApplyToLockedMachine { get; init; }
}
