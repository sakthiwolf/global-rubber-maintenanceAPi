using GlobalRubber.MMM.Api.Authorization;
using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Domain.Constants;
using Microsoft.AspNetCore.Mvc;

namespace GlobalRubber.MMM.Api.Controllers;

/// <summary>
/// Spare Part Reports (read-only) under the RPT_SPARE_PART module:
///   GET /api/v1/reports/spare-parts               - Spare Part Stock (master; paged, sortable)
///   GET /api/v1/reports/spare-parts/movements     - Stock Movement (the stock ledger)
///   GET /api/v1/reports/spare-parts/usage         - Spare Part Usage (cost at issue)
///   GET /api/v1/reports/spare-parts/low-stock     - Low Stock (summary + parts)
///   GET /api/v1/reports/spare-parts/valuation     - Stock Valuation (summary + parts, current unit cost)
///   GET /api/v1/reports/spare-parts/lookups       - filter options
///   GET .../export on each report                 - the whole filtered, sorted set as CSV
///
/// Reports and lookups require View; exports require Export (see EndpointAuthorizationCoverageTests). Nothing writes.
/// </summary>
[Route("api/v1/reports/spare-parts")]
public sealed class SparePartReportController : BaseApiController
{
    private readonly ISparePartReportService _service;

    public SparePartReportController(ISparePartReportService service)
    {
        _service = service;
    }

    [HttpGet]
    [RequirePermission(ModuleCodes.RptSparePart, PermissionAction.View)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<SparePartStockItemDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<PagedResult<SparePartStockItemDto>>>> GetStock(
        [FromQuery] SparePartStockReportQuery query, CancellationToken cancellationToken)
    {
        var result = await _service.GetStockAsync(query, cancellationToken);
        return Ok(ApiResponse<PagedResult<SparePartStockItemDto>>.Ok(result, "Spare part stock report retrieved successfully."));
    }

    [HttpGet("export")]
    [RequirePermission(ModuleCodes.RptSparePart, PermissionAction.Export)]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ExportStock([FromQuery] SparePartStockReportQuery query, CancellationToken cancellationToken) =>
        ToFile(await _service.ExportStockAsync(query, cancellationToken));

    [HttpGet("movements")]
    [RequirePermission(ModuleCodes.RptSparePart, PermissionAction.View)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<SparePartMovementItemDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<PagedResult<SparePartMovementItemDto>>>> GetMovements(
        [FromQuery] SparePartMovementReportQuery query, CancellationToken cancellationToken)
    {
        var result = await _service.GetMovementsAsync(query, cancellationToken);
        return Ok(ApiResponse<PagedResult<SparePartMovementItemDto>>.Ok(result, "Stock movement report retrieved successfully."));
    }

    [HttpGet("movements/export")]
    [RequirePermission(ModuleCodes.RptSparePart, PermissionAction.Export)]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ExportMovements([FromQuery] SparePartMovementReportQuery query, CancellationToken cancellationToken) =>
        ToFile(await _service.ExportMovementsAsync(query, cancellationToken));

    [HttpGet("usage")]
    [RequirePermission(ModuleCodes.RptSparePart, PermissionAction.View)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<SparePartUsageItemDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<PagedResult<SparePartUsageItemDto>>>> GetUsage(
        [FromQuery] SparePartUsageReportQuery query, CancellationToken cancellationToken)
    {
        var result = await _service.GetUsageAsync(query, cancellationToken);
        return Ok(ApiResponse<PagedResult<SparePartUsageItemDto>>.Ok(result, "Spare part usage report retrieved successfully."));
    }

    [HttpGet("usage/export")]
    [RequirePermission(ModuleCodes.RptSparePart, PermissionAction.Export)]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ExportUsage([FromQuery] SparePartUsageReportQuery query, CancellationToken cancellationToken) =>
        ToFile(await _service.ExportUsageAsync(query, cancellationToken));

    [HttpGet("low-stock")]
    [RequirePermission(ModuleCodes.RptSparePart, PermissionAction.View)]
    [ProducesResponseType(typeof(ApiResponse<SparePartLowStockReportDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<SparePartLowStockReportDto>>> GetLowStock(
        [FromQuery] SparePartStockReportQuery query, CancellationToken cancellationToken)
    {
        var result = await _service.GetLowStockAsync(query, cancellationToken);
        return Ok(ApiResponse<SparePartLowStockReportDto>.Ok(result, "Low stock report retrieved successfully."));
    }

    [HttpGet("low-stock/export")]
    [RequirePermission(ModuleCodes.RptSparePart, PermissionAction.Export)]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ExportLowStock([FromQuery] SparePartStockReportQuery query, CancellationToken cancellationToken) =>
        ToFile(await _service.ExportLowStockAsync(query, cancellationToken));

    [HttpGet("valuation")]
    [RequirePermission(ModuleCodes.RptSparePart, PermissionAction.View)]
    [ProducesResponseType(typeof(ApiResponse<SparePartValuationReportDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<SparePartValuationReportDto>>> GetValuation(
        [FromQuery] SparePartStockReportQuery query, CancellationToken cancellationToken)
    {
        var result = await _service.GetValuationAsync(query, cancellationToken);
        return Ok(ApiResponse<SparePartValuationReportDto>.Ok(result, "Stock valuation report retrieved successfully."));
    }

    [HttpGet("valuation/export")]
    [RequirePermission(ModuleCodes.RptSparePart, PermissionAction.Export)]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ExportValuation([FromQuery] SparePartStockReportQuery query, CancellationToken cancellationToken) =>
        ToFile(await _service.ExportValuationAsync(query, cancellationToken));

    /// <summary>Filter options for every tab, under the report's own View permission.</summary>
    [HttpGet("lookups")]
    [RequirePermission(ModuleCodes.RptSparePart, PermissionAction.View)]
    [ProducesResponseType(typeof(ApiResponse<SparePartReportLookupsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<SparePartReportLookupsDto>>> GetLookups(CancellationToken cancellationToken)
    {
        var lookups = await _service.GetLookupsAsync(cancellationToken);
        return Ok(ApiResponse<SparePartReportLookupsDto>.Ok(lookups, "Spare part report lookups retrieved successfully."));
    }

    private FileContentResult ToFile(ReportFile file) => File(file.Content, file.ContentType, file.FileName);
}
