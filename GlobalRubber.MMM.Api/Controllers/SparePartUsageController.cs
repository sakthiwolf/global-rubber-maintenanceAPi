using GlobalRubber.MMM.Api.Authorization;
using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Domain.Constants;
using Microsoft.AspNetCore.Mvc;

namespace GlobalRubber.MMM.Api.Controllers;

/// <summary>
/// Spare Part Usage endpoints (transactions.spare_part_usage_transaction + the stock ledger). Stock changes ONLY through a
/// posted usage (POST) or its reversal (DELETE - a reversal, never a physical delete); there is no stock-manipulation
/// endpoint and no edit. Every endpoint requires a TrnSparePartUsage permission via [RequirePermission] - never a
/// role-name check: View for reads, Add to post, Delete to reverse.
/// </summary>
[Route("api/v1/spare-part-usage")]
public sealed class SparePartUsageController : BaseApiController
{
    private readonly ISparePartUsageService _service;

    public SparePartUsageController(ISparePartUsageService service)
    {
        _service = service;
    }

    /// <summary>A page of usages (newest first), filtered by date range, spare part, machine, mold, maintenance type, status or a search on usage / maintenance number.</summary>
    [HttpGet]
    [RequirePermission(ModuleCodes.TrnSparePartUsage, PermissionAction.View)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<SparePartUsageDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<PagedResult<SparePartUsageDto>>>> GetAll([FromQuery] SparePartUsageListQuery query, CancellationToken cancellationToken)
    {
        var result = await _service.GetAllAsync(query, cancellationToken);
        return Ok(ApiResponse<PagedResult<SparePartUsageDto>>.Ok(result, "Spare part usage retrieved successfully."));
    }

    /// <summary>The Add form's lookups: active spare parts (unit, stock), open and due Machine PMs, open Mold PMs, active employees.</summary>
    [HttpGet("lookups")]
    [RequirePermission(ModuleCodes.TrnSparePartUsage, PermissionAction.View)]
    [ProducesResponseType(typeof(ApiResponse<SparePartUsageLookupsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<SparePartUsageLookupsDto>>> GetLookups(CancellationToken cancellationToken)
    {
        var lookups = await _service.GetLookupsAsync(cancellationToken);
        return Ok(ApiResponse<SparePartUsageLookupsDto>.Ok(lookups, "Spare part usage lookups retrieved successfully."));
    }

    /// <summary>One usage with its stock movements (ledger rows) and rowVersion. 404 if it does not exist.</summary>
    [HttpGet("{id:int}")]
    [RequirePermission(ModuleCodes.TrnSparePartUsage, PermissionAction.View)]
    [ProducesResponseType(typeof(ApiResponse<SparePartUsageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<SparePartUsageDto>>> GetById(int id, CancellationToken cancellationToken)
    {
        var usage = await _service.GetByIdAsync(id, cancellationToken);
        return Ok(ApiResponse<SparePartUsageDto>.Ok(usage, "Spare part usage retrieved successfully."));
    }

    /// <summary>
    /// Posts a usage against an open Machine PM (due) or Mold PM: deducts the stock and writes an Issue ledger row in one
    /// transaction. 201 when posted; 200 with the existing usage when the same requestId was already posted; 400
    /// validation; 404 unknown part / maintenance; 409 "Insufficient stock available." or a request id reused for other data.
    /// </summary>
    [HttpPost]
    [RequirePermission(ModuleCodes.TrnSparePartUsage, PermissionAction.Add)]
    [ProducesResponseType(typeof(ApiResponse<SparePartUsageDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<SparePartUsageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<SparePartUsageDto>>> Create([FromBody] CreateSparePartUsageRequest request, CancellationToken cancellationToken)
    {
        var (usage, created) = await _service.CreateAsync(
            request, GetAuthenticatedUserId(), HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);

        return created
            ? CreatedAtAction(nameof(GetById), new { id = usage.SparePartUsageId }, ApiResponse<SparePartUsageDto>.Ok(usage, "Spare part usage posted successfully."))
            : Ok(ApiResponse<SparePartUsageDto>.Ok(usage, "This spare part usage was already posted."));
    }

    /// <summary>
    /// REVERSES a posted usage (Status Reversed, stock restored, Reversal ledger row) - the record is kept. Requires a reason
    /// and the rowVersion from the last read: 409 if stale or already reversed.
    /// </summary>
    [HttpDelete("{id:int}")]
    [RequirePermission(ModuleCodes.TrnSparePartUsage, PermissionAction.Delete)]
    [ProducesResponseType(typeof(ApiResponse<SparePartUsageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<SparePartUsageDto>>> Reverse(int id, [FromBody] ReverseSparePartUsageRequest request, CancellationToken cancellationToken)
    {
        var usage = await _service.ReverseAsync(
            id, request, GetAuthenticatedUserId(), HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);

        return Ok(ApiResponse<SparePartUsageDto>.Ok(usage, "Spare part usage reversed successfully. The stock was restored."));
    }
}
