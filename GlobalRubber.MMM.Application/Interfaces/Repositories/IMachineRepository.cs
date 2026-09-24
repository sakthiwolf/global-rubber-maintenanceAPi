using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Domain.Entities;

namespace GlobalRubber.MMM.Application.Interfaces.Repositories;

public interface IMachineRepository
{
    /// <summary>A page of machines (name-ordered) with Department loaded.</summary>
    Task<(IReadOnlyList<Machine> Items, int TotalCount)> GetAllAsync(MachineListQuery query, CancellationToken cancellationToken);

    /// <summary>Loads a machine (with Department) without tracking; it carries its row_version.</summary>
    Task<Machine?> GetByIdAsync(int machineId, CancellationToken cancellationToken);

    /// <summary>Whether another ACTIVE machine already has this name (case-insensitive); inactive machines never count.</summary>
    Task<bool> ExistsActiveByNameAsync(string machineName, int? excludeMachineId, CancellationToken cancellationToken);

    /// <summary>
    /// Whether any machine (active or not) already has this serial number - UX_machine_master_serial_number is unique
    /// across every row with a serial number.
    /// </summary>
    Task<bool> ExistsBySerialNumberAsync(string serialNumber, int? excludeMachineId, CancellationToken cancellationToken);

    /// <summary>
    /// Inserts the machine and assigns its MachineCode from the MACHINE document sequence, both in ONE transaction: a
    /// failed insert rolls the sequence increment back. Navigations are not saved - only the foreign-key ids.
    /// </summary>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ConflictException">The serial number or issued code already exists.</exception>
    Task<Machine> AddAsync(Machine machine, CancellationToken cancellationToken);

    /// <summary>
    /// Saves the editable fields plus UpdatedAt/UpdatedBy of a machine loaded through <see cref="GetByIdAsync"/>. Never
    /// MachineCode, IsActive, OperationalStatus, the last/next maintenance dates, ResponsibleEngineerId (temporarily
    /// disabled - the stored value is preserved) or the creation columns.
    /// <paramref name="originalRowVersion"/> is the concurrency guard.
    /// </summary>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ConflictException">Modified by someone else since <paramref name="originalRowVersion"/>, or the serial number already exists.</exception>
    Task<Machine> UpdateAsync(Machine machine, byte[] originalRowVersion, CancellationToken cancellationToken);

    /// <summary>
    /// Soft deactivation: persists ONLY IsActive, UpdatedAt and UpdatedBy of a machine loaded through
    /// <see cref="GetByIdAsync"/>, guarded by the row_version it was read with. The row is never deleted.
    /// </summary>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ConflictException">Modified by someone else (or removed) after it was loaded.</exception>
    Task<Machine> DeactivateAsync(Machine machine, CancellationToken cancellationToken);
}
