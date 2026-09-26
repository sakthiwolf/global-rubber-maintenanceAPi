using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;

namespace GlobalRubber.MMM.Application.Interfaces;

/// <summary>
/// Production Entry (analysis 6.1). There is deliberately no update, cancel or delete: the analysis defines none and the
/// question is open (Q-14) - saving an entry changes the mold's usage and status, which cannot be reversed by a rule
/// nobody has specified yet.
/// </summary>
public interface IProductionEntryService
{
    Task<PagedResult<ProductionEntryDto>> GetAllAsync(ProductionEntryListQuery query, CancellationToken cancellationToken);

    /// <exception cref="GlobalRubber.MMM.Application.Common.NotFoundException">No entry exists with the given id.</exception>
    Task<ProductionEntryDto> GetByIdAsync(int productionEntryId, CancellationToken cancellationToken);

    /// <summary>Active machines, active products and molds (with life figures) for the entry form.</summary>
    Task<ProductionLookupsDto> GetLookupsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Saves an entry (Status = Saved, number from the PRODUCTION_ENTRY sequence), adds Production Qty to the mold's
    /// usage and re-evaluates the mold status - all in one transaction. Writes ProductionEntryCreated.
    /// </summary>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ValidationException">A field fails validation, a machine/product is inactive, or the mold does not belong to the product.</exception>
    /// <exception cref="GlobalRubber.MMM.Application.Common.NotFoundException">The machine, product or mold does not exist.</exception>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ConflictException">The mold has reached its replacement limit.</exception>
    Task<ProductionEntryDto> CreateAsync(CreateProductionEntryRequest request, int? actingUserId, string? ipAddress, CancellationToken cancellationToken);
}
