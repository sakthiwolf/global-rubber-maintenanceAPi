using GlobalRubber.MMM.Api.Authorization;
using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Domain.Constants;
using Microsoft.AspNetCore.Mvc;

namespace GlobalRubber.MMM.Api.Controllers;

/// <summary>
/// Mold Reports (read-only) under the RPT_MOLD module:
///   GET /api/v1/reports/molds                  - Mold List (paged)
///   GET /api/v1/reports/molds/usage            - current usage + production in a range (paged, sortable)
///   GET /api/v1/reports/molds/life-status      - life-state summary + molds (paged, sortable)
///   GET /api/v1/reports/molds/maintenance      - Mold PMs (paged)
///   GET /api/v1/reports/molds/replacement      - molds needing replacement or Retired (summary + paged, sortable)
///   GET /api/v1/reports/molds/lookups          - filter options
///   GET .../export on each report              - the whole filtered set as CSV
///
/// Reports and lookups require View; exports require Export (see EndpointAuthorizationCoverageTests). Nothing writes.
/// </summary>
[Route("api/v1/reports/molds")]
public sealed class MoldReportController : BaseApiController
{
    private readonly IMoldReportService _service;

    public MoldReportController(IMoldReportService service)
    {
        _service = service;
    }

    [HttpGet]
    [RequirePermission(ModuleCodes.RptMold, PermissionAction.View)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<MoldListReportItemDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<PagedResult<MoldListReportItemDto>>>> GetMoldList(
        [FromQuery] MoldListReportQuery query, CancellationToken cancellationToken)
    {
        var result = await _service.GetMoldListAsync(query, cancellationToken);
        return Ok(ApiResponse<PagedResult<MoldListReportItemDto>>.Ok(result, "Mold list report retrieved successfully."));
    }

    [HttpGet("export")]
    [RequirePermission(ModuleCodes.RptMold, PermissionAction.Export)]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ExportMoldList([FromQuery] MoldListReportQuery query, CancellationToken cancellationToken) =>
        ToFile(await _service.ExportMoldListAsync(query, cancellationToken));

    [HttpGet("usage")]
    [RequirePermission(ModuleCodes.RptMold, PermissionAction.View)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<MoldUsageReportItemDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<PagedResult<MoldUsageReportItemDto>>>> GetUsage(
        [FromQuery] MoldUsageReportQuery query, CancellationToken cancellationToken)
    {
        var result = await _service.GetUsageAsync(query, cancellationToken);
        return Ok(ApiResponse<PagedResult<MoldUsageReportItemDto>>.Ok(result, "Mold usage report retrieved successfully."));
    }

    [HttpGet("usage/export")]
    [RequirePermission(ModuleCodes.RptMold, PermissionAction.Export)]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ExportUsage([FromQuery] MoldUsageReportQuery query, CancellationToken cancellationToken) =>
        ToFile(await _service.ExportUsageAsync(query, cancellationToken));

    [HttpGet("life-status")]
    [RequirePermission(ModuleCodes.RptMold, PermissionAction.View)]
    [ProducesResponseType(typeof(ApiResponse<MoldLifeReportDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<MoldLifeReportDto>>> GetLifeStatus(
        [FromQuery] MoldLifeReportQuery query, CancellationToken cancellationToken)
    {
        var result = await _service.GetLifeStatusAsync(query, cancellationToken);
        return Ok(ApiResponse<MoldLifeReportDto>.Ok(result, "Mold life status report retrieved successfully."));
    }

    [HttpGet("life-status/export")]
    [RequirePermission(ModuleCodes.RptMold, PermissionAction.Export)]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ExportLifeStatus([FromQuery] MoldLifeReportQuery query, CancellationToken cancellationToken) =>
        ToFile(await _service.ExportLifeStatusAsync(query, cancellationToken));

    [HttpGet("maintenance")]
    [RequirePermission(ModuleCodes.RptMold, PermissionAction.View)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<MoldMaintenanceReportItemDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<PagedResult<MoldMaintenanceReportItemDto>>>> GetMaintenance(
        [FromQuery] MoldMaintenanceReportQuery query, CancellationToken cancellationToken)
    {
        var result = await _service.GetMaintenanceAsync(query, cancellationToken);
        return Ok(ApiResponse<PagedResult<MoldMaintenanceReportItemDto>>.Ok(result, "Mold maintenance report retrieved successfully."));
    }

    [HttpGet("maintenance/export")]
    [RequirePermission(ModuleCodes.RptMold, PermissionAction.Export)]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ExportMaintenance([FromQuery] MoldMaintenanceReportQuery query, CancellationToken cancellationToken) =>
        ToFile(await _service.ExportMaintenanceAsync(query, cancellationToken));

    [HttpGet("replacement")]
    [RequirePermission(ModuleCodes.RptMold, PermissionAction.View)]
    [ProducesResponseType(typeof(ApiResponse<MoldLifeReportDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<MoldLifeReportDto>>> GetReplacement(
        [FromQuery] MoldLifeReportQuery query, CancellationToken cancellationToken)
    {
        var result = await _service.GetReplacementAsync(query, cancellationToken);
        return Ok(ApiResponse<MoldLifeReportDto>.Ok(result, "Mold replacement report retrieved successfully."));
    }

    [HttpGet("replacement/export")]
    [RequirePermission(ModuleCodes.RptMold, PermissionAction.Export)]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ExportReplacement([FromQuery] MoldLifeReportQuery query, CancellationToken cancellationToken) =>
        ToFile(await _service.ExportReplacementAsync(query, cancellationToken));

    /// <summary>Filter options for every tab, under the report's own View permission (no master permissions needed).</summary>
    [HttpGet("lookups")]
    [RequirePermission(ModuleCodes.RptMold, PermissionAction.View)]
    [ProducesResponseType(typeof(ApiResponse<MoldReportLookupsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<MoldReportLookupsDto>>> GetLookups(CancellationToken cancellationToken)
    {
        var lookups = await _service.GetLookupsAsync(cancellationToken);
        return Ok(ApiResponse<MoldReportLookupsDto>.Ok(lookups, "Mold report lookups retrieved successfully."));
    }

    private FileContentResult ToFile(ReportFile file) => File(file.Content, file.ContentType, file.FileName);
}
