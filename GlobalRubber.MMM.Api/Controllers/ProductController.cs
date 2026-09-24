using GlobalRubber.MMM.Api.Authorization;
using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Domain.Constants;
using Microsoft.AspNetCore.Mvc;

namespace GlobalRubber.MMM.Api.Controllers;

/// <summary>
/// Product endpoints (masters.product_master): list, retrieve, create, update and deactivate. Every endpoint requires a
/// MasterProduct permission (View for reads, Add, Edit, Delete for deactivation) via [RequirePermission] - never a
/// role-name check. DELETE is a soft deactivation; nothing is ever physically deleted.
/// </summary>
[Route("api/v1/products")]
public sealed class ProductController : BaseApiController
{
    private readonly IProductService _productService;

    public ProductController(IProductService productService)
    {
        _productService = productService;
    }

    /// <summary>Returns a paged list of products, optionally filtered by search text (code/name) and active status.</summary>
    [HttpGet]
    [RequirePermission(ModuleCodes.MasterProduct, PermissionAction.View)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<ProductDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<PagedResult<ProductDto>>>> GetAll(
        [FromQuery] ProductListQuery query, CancellationToken cancellationToken)
    {
        var result = await _productService.GetAllAsync(query, cancellationToken);

        return Ok(ApiResponse<PagedResult<ProductDto>>.Ok(result, "Products retrieved successfully."));
    }

    /// <summary>Returns one product by id, including the rowVersion needed to update it. 404 if it does not exist.</summary>
    [HttpGet("{id:int}")]
    [RequirePermission(ModuleCodes.MasterProduct, PermissionAction.View)]
    [ProducesResponseType(typeof(ApiResponse<ProductDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<ProductDto>>> GetById(int id, CancellationToken cancellationToken)
    {
        var product = await _productService.GetByIdAsync(id, cancellationToken);

        return Ok(ApiResponse<ProductDto>.Ok(product, "Product retrieved successfully."));
    }

    /// <summary>
    /// Creates an active product; its code is issued from the PRODUCT document sequence. 400 for an invalid field; 409 if
    /// another active product has the same name.
    /// </summary>
    [HttpPost]
    [RequirePermission(ModuleCodes.MasterProduct, PermissionAction.Add)]
    [ProducesResponseType(typeof(ApiResponse<ProductDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<ProductDto>>> Create(
        [FromBody] CreateProductRequest request, CancellationToken cancellationToken)
    {
        var product = await _productService.CreateAsync(
            request, GetAuthenticatedUserId(), HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = product.ProductId }, ApiResponse<ProductDto>.Ok(product, "Product created successfully."));
    }

    /// <summary>
    /// Updates the editable fields (the code and status are not editable here). Requires the rowVersion from the last
    /// read: 409 if the product was modified since, or if another active product has the name.
    /// </summary>
    [HttpPut("{id:int}")]
    [RequirePermission(ModuleCodes.MasterProduct, PermissionAction.Edit)]
    [ProducesResponseType(typeof(ApiResponse<ProductDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<ProductDto>>> Update(
        int id, [FromBody] UpdateProductRequest request, CancellationToken cancellationToken)
    {
        var product = await _productService.UpdateAsync(
            id, request, GetAuthenticatedUserId(), HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);

        return Ok(ApiResponse<ProductDto>.Ok(product, "Product updated successfully."));
    }

    /// <summary>
    /// Deactivates a product - DELETE means SOFT deactivation (IsActive = false); the row is kept. 409 if it is already
    /// inactive.
    /// </summary>
    [HttpDelete("{id:int}")]
    [RequirePermission(ModuleCodes.MasterProduct, PermissionAction.Delete)]
    [ProducesResponseType(typeof(ApiResponse<ProductDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<ProductDto>>> Deactivate(int id, CancellationToken cancellationToken)
    {
        var product = await _productService.DeactivateAsync(
            id, GetAuthenticatedUserId(), HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);

        return Ok(ApiResponse<ProductDto>.Ok(product, "Product deactivated successfully."));
    }
}
