using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Domain.Entities;

namespace GlobalRubber.MMM.Application.Interfaces.Repositories;

public interface IMaintenanceTypeRepository
{
    Task<(IReadOnlyList<MaintenanceType> Items, int TotalCount)> GetAllAsync(MaintenanceTypeListQuery query, CancellationToken cancellationToken);

    /// <summary>Loads a maintenance type without tracking; it carries the row_version it was read with.</summary>
    Task<MaintenanceType?> GetByIdAsync(int maintenanceTypeId, CancellationToken cancellationToken);

    /// <summary>
    /// Whether another ACTIVE maintenance type already has this name (case-insensitive). Inactive ones never count.
    /// </summary>
    Task<bool> ExistsActiveByNameAsync(string name, int? excludeMaintenanceTypeId, CancellationToken cancellationToken);

    /// <summary>
    /// Inserts the maintenance type and assigns its code from the MAINTENANCE_TYPE document sequence, both in ONE
    /// transaction: a failed insert rolls the sequence increment back, so no number is burned.
    /// </summary>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ConflictException">The issued code already exists.</exception>
    Task<MaintenanceType> AddAsync(MaintenanceType maintenanceType, CancellationToken cancellationToken);

    /// <summary>
    /// Saves the name, applies-to, UpdatedAt and UpdatedBy of a maintenance type loaded through <see cref="GetByIdAsync"/>.
    /// Never the code, IsActive or the creation columns. <paramref name="originalRowVersion"/> is the concurrency guard.
    /// </summary>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ConflictException">Modified by someone else (or removed) since <paramref name="originalRowVersion"/>.</exception>
    Task<MaintenanceType> UpdateAsync(MaintenanceType maintenanceType, byte[] originalRowVersion, CancellationToken cancellationToken);

    /// <summary>
    /// Soft deactivation: persists ONLY IsActive, UpdatedAt and UpdatedBy of a maintenance type loaded through
    /// <see cref="GetByIdAsync"/>, guarded by the row_version it was read with. The row is never deleted.
    /// </summary>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ConflictException">Modified by someone else (or removed) after it was loaded.</exception>
    Task<MaintenanceType> DeactivateAsync(MaintenanceType maintenanceType, CancellationToken cancellationToken);
}
