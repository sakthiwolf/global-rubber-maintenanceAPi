using GlobalRubber.MMM.Api.Authorization;
using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Domain.Constants;
using Microsoft.AspNetCore.Mvc;

namespace GlobalRubber.MMM.Api.Controllers;

/// <summary>
/// Vendor endpoints (masters.vendor_master): list, retrieve, create, update and deactivate. Every endpoint requires a
/// MasterVendor permission (View for reads, Add, Edit, Delete for deactivation) via [RequirePermission] - never a
/// role-name check. DELETE is a soft deactivation; nothing is ever physically deleted.
/// </summary>
[Route("api/v1/vendors")]
public sealed class VendorController : BaseApiController
{
    private readonly IVendorService _vendorService;

    public VendorController(IVendorService vendorService)
    {
        _vendorService = vendorService;
    }

    /// <summary>Returns a paged list of vendors, optionally filtered by search text (code/name) and active status.</summary>
    [HttpGet]
    [RequirePermission(ModuleCodes.MasterVendor, PermissionAction.View)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<VendorDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<PagedResult<VendorDto>>>> GetAll(
        [FromQuery] VendorListQuery query, CancellationToken cancellationToken)
    {
        var result = await _vendorService.GetAllAsync(query, cancellationToken);

        return Ok(ApiResponse<PagedResult<VendorDto>>.Ok(result, "Vendors retrieved successfully."));
    }

    /// <summary>Returns one vendor by id, including the rowVersion needed to update it. 404 if it does not exist.</summary>
    [HttpGet("{id:int}")]
    [RequirePermission(ModuleCodes.MasterVendor, PermissionAction.View)]
    [ProducesResponseType(typeof(ApiResponse<VendorDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<VendorDto>>> GetById(int id, CancellationToken cancellationToken)
    {
        var vendor = await _vendorService.GetByIdAsync(id, cancellationToken);

        return Ok(ApiResponse<VendorDto>.Ok(vendor, "Vendor retrieved successfully."));
    }

    /// <summary>
    /// Creates an active vendor; its code is issued from the VENDOR document sequence. 400 for an invalid field; 409 if
    /// another active vendor has the same name.
    /// </summary>
    [HttpPost]
    [RequirePermission(ModuleCodes.MasterVendor, PermissionAction.Add)]
    [ProducesResponseType(typeof(ApiResponse<VendorDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<VendorDto>>> Create(
        [FromBody] CreateVendorRequest request, CancellationToken cancellationToken)
    {
        var vendor = await _vendorService.CreateAsync(
            request, GetAuthenticatedUserId(), HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = vendor.VendorId }, ApiResponse<VendorDto>.Ok(vendor, "Vendor created successfully."));
    }

    /// <summary>
    /// Updates the editable fields (the code and status are not editable here). Requires the rowVersion from the last
    /// read: 409 if the vendor was modified since, or if another active vendor has the name.
    /// </summary>
    [HttpPut("{id:int}")]
    [RequirePermission(ModuleCodes.MasterVendor, PermissionAction.Edit)]
    [ProducesResponseType(typeof(ApiResponse<VendorDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<VendorDto>>> Update(
        int id, [FromBody] UpdateVendorRequest request, CancellationToken cancellationToken)
    {
        var vendor = await _vendorService.UpdateAsync(
            id, request, GetAuthenticatedUserId(), HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);

        return Ok(ApiResponse<VendorDto>.Ok(vendor, "Vendor updated successfully."));
    }

    /// <summary>
    /// Deactivates a vendor - DELETE means SOFT deactivation (IsActive = false); the row is kept. 409 if it is already
    /// inactive.
    /// </summary>
    [HttpDelete("{id:int}")]
    [RequirePermission(ModuleCodes.MasterVendor, PermissionAction.Delete)]
    [ProducesResponseType(typeof(ApiResponse<VendorDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<VendorDto>>> Deactivate(int id, CancellationToken cancellationToken)
    {
        var vendor = await _vendorService.DeactivateAsync(
            id, GetAuthenticatedUserId(), HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);

        return Ok(ApiResponse<VendorDto>.Ok(vendor, "Vendor deactivated successfully."));
    }
}
