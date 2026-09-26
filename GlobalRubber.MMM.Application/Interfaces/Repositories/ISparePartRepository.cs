using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Domain.Entities;

namespace GlobalRubber.MMM.Application.Interfaces.Repositories;

public interface ISparePartRepository
{
    /// <summary>A page of spare parts (name-ordered) with Machine and Vendor loaded.</summary>
    Task<(IReadOnlyList<SparePart> Items, int TotalCount)> GetAllAsync(SparePartListQuery query, CancellationToken cancellationToken);

    /// <summary>Loads a spare part (with Machine and Vendor) without tracking; it carries the row_version it was read with.</summary>
    Task<SparePart?> GetByIdAsync(int sparePartId, CancellationToken cancellationToken);

    /// <summary>Whether another ACTIVE spare part already has this name (case-insensitive). Inactive ones never count.</summary>
    Task<bool> ExistsActiveByNameAsync(string name, int? excludeSparePartId, CancellationToken cancellationToken);

    /// <summary>
    /// Inserts the spare part and assigns its code from the SPARE_PART document sequence, both in ONE transaction: a failed
    /// insert rolls the sequence increment back. Navigations are not saved - only the FK ids. StockStatus is read back.
    /// </summary>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ConflictException">The issued code already exists.</exception>
    /// <summary>
    /// Inserts the part (code from the SPARE_PART sequence) and, in the same transaction, its Opening stock ledger row
    /// (<paramref name="openingEntry"/>; spare_part_id / reference id are filled in here).
    /// </summary>
    Task<SparePart> AddAsync(SparePart sparePart, SparePartStockTransaction openingEntry, CancellationToken cancellationToken);

    /// <summary>
    /// Saves the editable fields (including CurrentStock - owned by the master, Q-06) plus UpdatedAt/UpdatedBy of a spare
    /// part loaded through <see cref="GetByIdAsync"/>. Never the code, IsActive or the creation columns; StockStatus is
    /// recomputed by SQL Server and read back. <paramref name="originalRowVersion"/> is the concurrency guard.
    /// </summary>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ConflictException">Modified by someone else (or removed) since <paramref name="originalRowVersion"/>.</exception>
    /// <remarks>
    /// When <paramref name="stockEntry"/> is given (the current stock changed), its Adjustment ledger row is written in the
    /// SAME transaction as the update - the previous stock it records is the one the caller's row version guarantees.
    /// </remarks>
    Task<SparePart> UpdateAsync(SparePart sparePart, byte[] originalRowVersion, SparePartStockTransaction? stockEntry, CancellationToken cancellationToken);

    /// <summary>
    /// Soft deactivation: persists ONLY IsActive, UpdatedAt and UpdatedBy of a spare part loaded through
    /// <see cref="GetByIdAsync"/>, guarded by the row_version it was read with. The row is never deleted.
    /// </summary>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ConflictException">Modified by someone else (or removed) after it was loaded.</exception>
    Task<SparePart> DeactivateAsync(SparePart sparePart, CancellationToken cancellationToken);
}
