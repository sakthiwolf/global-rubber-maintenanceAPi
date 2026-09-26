using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Domain.Entities;

namespace GlobalRubber.MMM.Application.Interfaces.Repositories;

public interface IMoldRepository
{
    /// <summary>A page of molds (name-ordered) with Product and ResponsibleEmployee loaded.</summary>
    Task<(IReadOnlyList<Mold> Items, int TotalCount)> GetAllAsync(MoldListQuery query, CancellationToken cancellationToken);

    /// <summary>Loads a mold (with Product and ResponsibleEmployee) without tracking; it carries its row_version.</summary>
    Task<Mold?> GetByIdAsync(int moldId, CancellationToken cancellationToken);

    /// <summary>
    /// Whether another mold that is not Retired already has this name (case-insensitive). Retired molds never count -
    /// 'Retired' is the mold equivalent of inactive.
    /// </summary>
    Task<bool> ExistsNonRetiredByNameAsync(string moldName, int? excludeMoldId, CancellationToken cancellationToken);

    /// <summary>Whether any mold (retired or not) already has this serial number - UX_mold_master_serial_number.</summary>
    Task<bool> ExistsBySerialNumberAsync(string serialNumber, int? excludeMoldId, CancellationToken cancellationToken);

    /// <summary>
    /// Inserts the mold and assigns its MoldCode from the MOLD document sequence, both in ONE transaction: a failed insert
    /// rolls the sequence increment back. Navigations are not saved - only the foreign-key ids. LifeState is read back.
    /// </summary>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ConflictException">The serial number or issued code already exists.</exception>
    Task<Mold> AddAsync(Mold mold, CancellationToken cancellationToken);

    /// <summary>
    /// Saves the editable fields (including Status and CurrentUsageShots) plus UpdatedAt/UpdatedBy of a mold loaded
    /// through <see cref="GetByIdAsync"/>. Never MoldCode or the creation columns; LifeState is recomputed by SQL Server
    /// and read back. <paramref name="originalRowVersion"/> is the concurrency guard. PmCycleStartShots is never written
    /// here (system-managed). <paramref name="afterSave"/> runs in the SAME transaction after the update (the row is then
    /// exclusively locked) - the automatic Mold PM evaluation of the saved values.
    /// </summary>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ConflictException">Modified by someone else since <paramref name="originalRowVersion"/>, or the serial number already exists.</exception>
    Task<Mold> UpdateAsync(Mold mold, byte[] originalRowVersion, Func<Mold, CancellationToken, Task>? afterSave, CancellationToken cancellationToken);

    /// <summary>
    /// Retires the mold: persists ONLY Status, UpdatedAt and UpdatedBy of a mold loaded through <see cref="GetByIdAsync"/>,
    /// guarded by the row_version it was read with. The row is never deleted.
    /// </summary>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ConflictException">Modified by someone else (or removed) after it was loaded.</exception>
    Task<Mold> RetireAsync(Mold mold, CancellationToken cancellationToken);
}
