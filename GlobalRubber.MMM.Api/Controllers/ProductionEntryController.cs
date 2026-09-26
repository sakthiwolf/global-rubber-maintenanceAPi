using GlobalRubber.MMM.Api.Authorization;
using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Domain.Constants;
using Microsoft.AspNetCore.Mvc;

namespace GlobalRubber.MMM.Api.Controllers;

/// <summary>
/// Production entry endpoints (transactions.production_entry_transaction): list, retrieve, form lookups and create -
/// exactly the analysis' API (18.4). Every endpoint requires a TrnProductionEntry permission (View for reads, Add for
/// create) via [RequirePermission] - never a role-name check. There is no PUT/DELETE: the analysis defines no edit or
/// cancel for production entries (6.1, Q-14).
/// </summary>
[Route("api/v1/production-entries")]
public sealed class ProductionEntryController : BaseApiController
{
    private readonly IProductionEntryService _productionEntryService;

    public ProductionEntryController(IProductionEntryService productionEntryService)
    {
        _productionEntryService = productionEntryService;
    }

    /// <summary>Returns a page of production entries (newest first), optionally filtered by entry number, date range, machine and mold.</summary>
    [HttpGet]
    [RequirePermission(ModuleCodes.TrnProductionEntry, PermissionAction.View)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<ProductionEntryDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<PagedResult<ProductionEntryDto>>>> GetAll(
        [FromQuery] ProductionEntryListQuery query, CancellationToken cancellationToken)
    {
        var result = await _productionEntryService.GetAllAsync(query, cancellationToken);

        return Ok(ApiResponse<PagedResult<ProductionEntryDto>>.Ok(result, "Production entries retrieved successfully."));
    }

    /// <summary>
    /// Dropdown data for the entry form: active machines, active products, and molds with their life figures. Served under
    /// the Production Entry permission (a Production User has no Product master permission) - the same names the list
    /// already shows, nothing more.
    /// </summary>
    [HttpGet("lookups")]
    [RequirePermission(ModuleCodes.TrnProductionEntry, PermissionAction.View)]
    [ProducesResponseType(typeof(ApiResponse<ProductionLookupsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<ProductionLookupsDto>>> GetLookups(CancellationToken cancellationToken)
    {
        var lookups = await _productionEntryService.GetLookupsAsync(cancellationToken);

        return Ok(ApiResponse<ProductionLookupsDto>.Ok(lookups, "Production entry lookups retrieved successfully."));
    }

    /// <summary>Returns one production entry by id. 404 if it does not exist.</summary>
    [HttpGet("{id:int}")]
    [RequirePermission(ModuleCodes.TrnProductionEntry, PermissionAction.View)]
    [ProducesResponseType(typeof(ApiResponse<ProductionEntryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<ProductionEntryDto>>> GetById(int id, CancellationToken cancellationToken)
    {
        var entry = await _productionEntryService.GetByIdAsync(id, cancellationToken);

        return Ok(ApiResponse<ProductionEntryDto>.Ok(entry, "Production entry retrieved successfully."));
    }

    /// <summary>
    /// Saves a production entry (number from the PRODUCTION_ENTRY sequence) and, in the same transaction, adds its
    /// quantity to the mold's usage and re-evaluates the mold status. 400 for an invalid field, an inactive machine/product
    /// or a mold of another product; 404 for an unknown machine/product/mold; 409 when the mold has reached its
    /// replacement limit.
    /// </summary>
    [HttpPost]
    [RequirePermission(ModuleCodes.TrnProductionEntry, PermissionAction.Add)]
    [ProducesResponseType(typeof(ApiResponse<ProductionEntryDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<ProductionEntryDto>>> Create(
        [FromBody] CreateProductionEntryRequest request, CancellationToken cancellationToken)
    {
        var entry = await _productionEntryService.CreateAsync(
            request, GetAuthenticatedUserId(), HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = entry.ProductionEntryId }, ApiResponse<ProductionEntryDto>.Ok(entry, "Production entry saved successfully."));
    }
}
