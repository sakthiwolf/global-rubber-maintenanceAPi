using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Domain.Entities;

namespace GlobalRubber.MMM.Application.Interfaces.Repositories;

public interface IProductionEntryRepository
{
    /// <summary>A page of entries (machine, product and mold included), newest first.</summary>
    Task<(IReadOnlyList<ProductionEntry> Items, int TotalCount)> GetAllAsync(ProductionEntryListQuery request, CancellationToken cancellationToken);

    /// <summary>One entry with machine, product and mold, untracked; null if it does not exist.</summary>
    Task<ProductionEntry?> GetByIdAsync(int productionEntryId, CancellationToken cancellationToken);

    /// <summary>Active machines, active products and the molds of active products, for the entry form.</summary>
    Task<ProductionLookupsDto> GetLookupsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Saves an entry and its mold-usage change as ONE unit (analysis BR-10 to BR-12, 13.x): inside one transaction the
    /// mold row is read with an update lock, <paramref name="applyToLockedMold"/> (the service's business rules) checks
    /// it and sets the entry's usage snapshot and the mold's new usage/status, the entry number is issued from the
    /// PRODUCTION_ENTRY sequence, and the entry and the mold are written. If anything throws, nothing is written -
    /// not the entry, not the mold, not the sequence number. <paramref name="afterSave"/> then runs in the SAME transaction
    /// on the saved, still-locked mold (the automatic Mold PM evaluation) before the commit.
    /// </summary>
    /// <exception cref="GlobalRubber.MMM.Application.Common.NotFoundException">The mold no longer exists.</exception>
    Task<ProductionEntry> AddAsync(
        ProductionEntry entry, Action<Mold> applyToLockedMold, Func<Mold, CancellationToken, Task>? afterSave, CancellationToken cancellationToken);
}
