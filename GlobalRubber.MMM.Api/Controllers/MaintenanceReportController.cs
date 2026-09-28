using GlobalRubber.MMM.Api.Authorization;
using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Domain.Constants;
using Microsoft.AspNetCore.Mvc;

namespace GlobalRubber.MMM.Api.Controllers;

/// <summary>
/// Maintenance Reports (read-only) under the RPT_MAINTENANCE module, over Machine PMs and Mold PMs:
///   GET /api/v1/reports/maintenance/schedule     - open PMs (paged, sortable)
///   GET /api/v1/reports/maintenance/completed    - completed PMs
///   GET /api/v1/reports/maintenance/overdue      - overdue summary + overdue PMs (each workflow's own rule)
///   GET /api/v1/reports/maintenance/history      - every PM
///   GET /api/v1/reports/maintenance/lookups      - filter options
///   GET .../export on each report                - the whole filtered, sorted set as CSV
///
/// There is no GET on the bare route: it would only duplicate one of the four tabs. Reports and lookups require View;
/// exports require Export (see EndpointAuthorizationCoverageTests). Nothing writes.
/// </summary>
[Route("api/v1/reports/maintenance")]
public sealed class MaintenanceReportController : BaseApiController
{
    private readonly IMaintenanceReportService _service;

    public MaintenanceReportController(IMaintenanceReportService service)
    {
        _service = service;
    }

    [HttpGet("schedule")]
    [RequirePermission(ModuleCodes.RptMaintenance, PermissionAction.View)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<MaintenanceReportItemDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<PagedResult<MaintenanceReportItemDto>>>> GetSchedule(
        [FromQuery] MaintenanceReportQuery query, CancellationToken cancellationToken)
    {
        var result = await _service.GetScheduleAsync(query, cancellationToken);
        return Ok(ApiResponse<PagedResult<MaintenanceReportItemDto>>.Ok(result, "PM schedule report retrieved successfully."));
    }

    [HttpGet("schedule/export")]
    [RequirePermission(ModuleCodes.RptMaintenance, PermissionAction.Export)]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ExportSchedule([FromQuery] MaintenanceReportQuery query, CancellationToken cancellationToken) =>
        ToFile(await _service.ExportScheduleAsync(query, cancellationToken));

    [HttpGet("completed")]
    [RequirePermission(ModuleCodes.RptMaintenance, PermissionAction.View)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<MaintenanceReportItemDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<PagedResult<MaintenanceReportItemDto>>>> GetCompleted(
        [FromQuery] MaintenanceReportQuery query, CancellationToken cancellationToken)
    {
        var result = await _service.GetCompletedAsync(query, cancellationToken);
        return Ok(ApiResponse<PagedResult<MaintenanceReportItemDto>>.Ok(result, "Completed PM report retrieved successfully."));
    }

    [HttpGet("completed/export")]
    [RequirePermission(ModuleCodes.RptMaintenance, PermissionAction.Export)]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ExportCompleted([FromQuery] MaintenanceReportQuery query, CancellationToken cancellationToken) =>
        ToFile(await _service.ExportCompletedAsync(query, cancellationToken));

    [HttpGet("overdue")]
    [RequirePermission(ModuleCodes.RptMaintenance, PermissionAction.View)]
    [ProducesResponseType(typeof(ApiResponse<MaintenanceOverdueReportDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<MaintenanceOverdueReportDto>>> GetOverdue(
        [FromQuery] MaintenanceReportQuery query, CancellationToken cancellationToken)
    {
        var result = await _service.GetOverdueAsync(query, cancellationToken);
        return Ok(ApiResponse<MaintenanceOverdueReportDto>.Ok(result, "Overdue PM report retrieved successfully."));
    }

    [HttpGet("overdue/export")]
    [RequirePermission(ModuleCodes.RptMaintenance, PermissionAction.Export)]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ExportOverdue([FromQuery] MaintenanceReportQuery query, CancellationToken cancellationToken) =>
        ToFile(await _service.ExportOverdueAsync(query, cancellationToken));

    [HttpGet("history")]
    [RequirePermission(ModuleCodes.RptMaintenance, PermissionAction.View)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<MaintenanceReportItemDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<PagedResult<MaintenanceReportItemDto>>>> GetHistory(
        [FromQuery] MaintenanceReportQuery query, CancellationToken cancellationToken)
    {
        var result = await _service.GetHistoryAsync(query, cancellationToken);
        return Ok(ApiResponse<PagedResult<MaintenanceReportItemDto>>.Ok(result, "Maintenance history report retrieved successfully."));
    }

    [HttpGet("history/export")]
    [RequirePermission(ModuleCodes.RptMaintenance, PermissionAction.Export)]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ExportHistory([FromQuery] MaintenanceReportQuery query, CancellationToken cancellationToken) =>
        ToFile(await _service.ExportHistoryAsync(query, cancellationToken));

    /// <summary>Filter options for every tab, under the report's own View permission.</summary>
    [HttpGet("lookups")]
    [RequirePermission(ModuleCodes.RptMaintenance, PermissionAction.View)]
    [ProducesResponseType(typeof(ApiResponse<MaintenanceReportLookupsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<MaintenanceReportLookupsDto>>> GetLookups(CancellationToken cancellationToken)
    {
        var lookups = await _service.GetLookupsAsync(cancellationToken);
        return Ok(ApiResponse<MaintenanceReportLookupsDto>.Ok(lookups, "Maintenance report lookups retrieved successfully."));
    }

    private FileContentResult ToFile(ReportFile file) => File(file.Content, file.ContentType, file.FileName);
}
