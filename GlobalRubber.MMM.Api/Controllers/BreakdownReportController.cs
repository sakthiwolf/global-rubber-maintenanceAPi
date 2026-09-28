using GlobalRubber.MMM.Api.Authorization;
using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Domain.Constants;
using Microsoft.AspNetCore.Mvc;

namespace GlobalRubber.MMM.Api.Controllers;

/// <summary>
/// Breakdown Reports (read-only) under the RPT_BREAKDOWN module:
///   GET /api/v1/reports/breakdowns              - Breakdown List (paged, sortable)
///   GET /api/v1/reports/breakdowns/analysis     - totals + breakdown counts grouped by machine / type / department / ...
///   GET /api/v1/reports/breakdowns/downtime     - downtime summary + breakdowns (paged, sortable)
///   GET /api/v1/reports/breakdowns/history      - resolved breakdowns with their stage timestamps (paged, sortable)
///   GET /api/v1/reports/breakdowns/lookups      - filter options
///   GET .../export on each report               - the whole filtered, sorted set as CSV
///
/// Reports and lookups require View; exports require Export (see EndpointAuthorizationCoverageTests). Nothing writes.
/// </summary>
[Route("api/v1/reports/breakdowns")]
public sealed class BreakdownReportController : BaseApiController
{
    private readonly IBreakdownReportService _service;

    public BreakdownReportController(IBreakdownReportService service)
    {
        _service = service;
    }

    [HttpGet]
    [RequirePermission(ModuleCodes.RptBreakdown, PermissionAction.View)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<MachineBreakdownReportItemDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<PagedResult<MachineBreakdownReportItemDto>>>> GetList(
        [FromQuery] MachineBreakdownReportQuery query, CancellationToken cancellationToken)
    {
        var result = await _service.GetListAsync(query, cancellationToken);
        return Ok(ApiResponse<PagedResult<MachineBreakdownReportItemDto>>.Ok(result, "Breakdown list report retrieved successfully."));
    }

    [HttpGet("export")]
    [RequirePermission(ModuleCodes.RptBreakdown, PermissionAction.Export)]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ExportList([FromQuery] MachineBreakdownReportQuery query, CancellationToken cancellationToken) =>
        ToFile(await _service.ExportListAsync(query, cancellationToken));

    [HttpGet("analysis")]
    [RequirePermission(ModuleCodes.RptBreakdown, PermissionAction.View)]
    [ProducesResponseType(typeof(ApiResponse<BreakdownAnalysisReportDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<BreakdownAnalysisReportDto>>> GetAnalysis(
        [FromQuery] BreakdownAnalysisReportQuery query, CancellationToken cancellationToken)
    {
        var result = await _service.GetAnalysisAsync(query, cancellationToken);
        return Ok(ApiResponse<BreakdownAnalysisReportDto>.Ok(result, "Breakdown analysis report retrieved successfully."));
    }

    [HttpGet("analysis/export")]
    [RequirePermission(ModuleCodes.RptBreakdown, PermissionAction.Export)]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ExportAnalysis([FromQuery] BreakdownAnalysisReportQuery query, CancellationToken cancellationToken) =>
        ToFile(await _service.ExportAnalysisAsync(query, cancellationToken));

    [HttpGet("downtime")]
    [RequirePermission(ModuleCodes.RptBreakdown, PermissionAction.View)]
    [ProducesResponseType(typeof(ApiResponse<MachineDowntimeReportDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<MachineDowntimeReportDto>>> GetDowntime(
        [FromQuery] MachineBreakdownReportQuery query, CancellationToken cancellationToken)
    {
        var result = await _service.GetDowntimeAsync(query, cancellationToken);
        return Ok(ApiResponse<MachineDowntimeReportDto>.Ok(result, "Downtime analysis report retrieved successfully."));
    }

    [HttpGet("downtime/export")]
    [RequirePermission(ModuleCodes.RptBreakdown, PermissionAction.Export)]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ExportDowntime([FromQuery] MachineBreakdownReportQuery query, CancellationToken cancellationToken) =>
        ToFile(await _service.ExportDowntimeAsync(query, cancellationToken));

    [HttpGet("history")]
    [RequirePermission(ModuleCodes.RptBreakdown, PermissionAction.View)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<MachineBreakdownReportItemDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<PagedResult<MachineBreakdownReportItemDto>>>> GetHistory(
        [FromQuery] MachineBreakdownReportQuery query, CancellationToken cancellationToken)
    {
        var result = await _service.GetHistoryAsync(query, cancellationToken);
        return Ok(ApiResponse<PagedResult<MachineBreakdownReportItemDto>>.Ok(result, "Breakdown history report retrieved successfully."));
    }

    [HttpGet("history/export")]
    [RequirePermission(ModuleCodes.RptBreakdown, PermissionAction.Export)]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ExportHistory([FromQuery] MachineBreakdownReportQuery query, CancellationToken cancellationToken) =>
        ToFile(await _service.ExportHistoryAsync(query, cancellationToken));

    /// <summary>Filter options for every tab, under the report's own View permission.</summary>
    [HttpGet("lookups")]
    [RequirePermission(ModuleCodes.RptBreakdown, PermissionAction.View)]
    [ProducesResponseType(typeof(ApiResponse<BreakdownReportLookupsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<BreakdownReportLookupsDto>>> GetLookups(CancellationToken cancellationToken)
    {
        var lookups = await _service.GetLookupsAsync(cancellationToken);
        return Ok(ApiResponse<BreakdownReportLookupsDto>.Ok(lookups, "Breakdown report lookups retrieved successfully."));
    }

    private FileContentResult ToFile(ReportFile file) => File(file.Content, file.ContentType, file.FileName);
}
