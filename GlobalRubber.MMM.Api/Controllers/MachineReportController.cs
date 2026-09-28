using GlobalRubber.MMM.Api.Authorization;
using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Domain.Constants;
using Microsoft.AspNetCore.Mvc;

namespace GlobalRubber.MMM.Api.Controllers;

/// <summary>
/// Machine Reports (read-only) under the RPT_MACHINE module:
///   GET /api/v1/reports/machines                            - Machine List (paged)
///   GET /api/v1/reports/machines/maintenance-history        - Machine PM occurrences (paged)
///   GET /api/v1/reports/machines/breakdowns                 - breakdowns (paged, sortable)
///   GET /api/v1/reports/machines/downtime                   - downtime summary + breakdown rows (paged, sortable)
///   GET /api/v1/reports/machines/lookups                    - filter options
///   GET .../export on each report                           - the whole filtered set as CSV
///
/// Reports and lookups require View; exports require Export (a GET gated by Export - see
/// EndpointAuthorizationCoverageTests). No endpoint writes anything.
/// </summary>
[Route("api/v1/reports/machines")]
public sealed class MachineReportController : BaseApiController
{
    private readonly IMachineReportService _service;

    public MachineReportController(IMachineReportService service)
    {
        _service = service;
    }

    // ================================================================ Machine List

    [HttpGet]
    [RequirePermission(ModuleCodes.RptMachine, PermissionAction.View)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<MachineListReportItemDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<PagedResult<MachineListReportItemDto>>>> GetMachineList(
        [FromQuery] MachineListReportQuery query, CancellationToken cancellationToken)
    {
        var result = await _service.GetMachineListAsync(query, cancellationToken);
        return Ok(ApiResponse<PagedResult<MachineListReportItemDto>>.Ok(result, "Machine list report retrieved successfully."));
    }

    [HttpGet("export")]
    [RequirePermission(ModuleCodes.RptMachine, PermissionAction.Export)]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ExportMachineList([FromQuery] MachineListReportQuery query, CancellationToken cancellationToken) =>
        ToFile(await _service.ExportMachineListAsync(query, cancellationToken));

    // ================================================================ Maintenance History

    [HttpGet("maintenance-history")]
    [RequirePermission(ModuleCodes.RptMachine, PermissionAction.View)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<MachineMaintenanceHistoryReportItemDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<PagedResult<MachineMaintenanceHistoryReportItemDto>>>> GetMaintenanceHistory(
        [FromQuery] MachineMaintenanceHistoryReportQuery query, CancellationToken cancellationToken)
    {
        var result = await _service.GetMaintenanceHistoryAsync(query, cancellationToken);
        return Ok(ApiResponse<PagedResult<MachineMaintenanceHistoryReportItemDto>>.Ok(result, "Maintenance history report retrieved successfully."));
    }

    [HttpGet("maintenance-history/export")]
    [RequirePermission(ModuleCodes.RptMachine, PermissionAction.Export)]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ExportMaintenanceHistory(
        [FromQuery] MachineMaintenanceHistoryReportQuery query, CancellationToken cancellationToken) =>
        ToFile(await _service.ExportMaintenanceHistoryAsync(query, cancellationToken));

    // ================================================================ Breakdown

    [HttpGet("breakdowns")]
    [RequirePermission(ModuleCodes.RptMachine, PermissionAction.View)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<MachineBreakdownReportItemDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<PagedResult<MachineBreakdownReportItemDto>>>> GetBreakdowns(
        [FromQuery] MachineBreakdownReportQuery query, CancellationToken cancellationToken)
    {
        var result = await _service.GetBreakdownsAsync(query, cancellationToken);
        return Ok(ApiResponse<PagedResult<MachineBreakdownReportItemDto>>.Ok(result, "Breakdown report retrieved successfully."));
    }

    [HttpGet("breakdowns/export")]
    [RequirePermission(ModuleCodes.RptMachine, PermissionAction.Export)]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ExportBreakdowns([FromQuery] MachineBreakdownReportQuery query, CancellationToken cancellationToken) =>
        ToFile(await _service.ExportBreakdownsAsync(query, cancellationToken));

    // ================================================================ Downtime

    [HttpGet("downtime")]
    [RequirePermission(ModuleCodes.RptMachine, PermissionAction.View)]
    [ProducesResponseType(typeof(ApiResponse<MachineDowntimeReportDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<MachineDowntimeReportDto>>> GetDowntime(
        [FromQuery] MachineBreakdownReportQuery query, CancellationToken cancellationToken)
    {
        var result = await _service.GetDowntimeAsync(query, cancellationToken);
        return Ok(ApiResponse<MachineDowntimeReportDto>.Ok(result, "Downtime report retrieved successfully."));
    }

    [HttpGet("downtime/export")]
    [RequirePermission(ModuleCodes.RptMachine, PermissionAction.Export)]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ExportDowntime([FromQuery] MachineBreakdownReportQuery query, CancellationToken cancellationToken) =>
        ToFile(await _service.ExportDowntimeAsync(query, cancellationToken));

    // ================================================================ Lookups

    /// <summary>Filter options for every tab, under the report's own View permission (no master permissions needed).</summary>
    [HttpGet("lookups")]
    [RequirePermission(ModuleCodes.RptMachine, PermissionAction.View)]
    [ProducesResponseType(typeof(ApiResponse<MachineReportLookupsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<MachineReportLookupsDto>>> GetLookups(CancellationToken cancellationToken)
    {
        var lookups = await _service.GetLookupsAsync(cancellationToken);
        return Ok(ApiResponse<MachineReportLookupsDto>.Ok(lookups, "Machine report lookups retrieved successfully."));
    }

    private FileContentResult ToFile(ReportFile file) => File(file.Content, file.ContentType, file.FileName);
}
