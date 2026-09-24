using GlobalRubber.MMM.Application.Common;
using Microsoft.AspNetCore.Mvc;

namespace GlobalRubber.MMM.Api.Controllers;

/// <summary>
/// Development-time verification endpoint(s) for the API foundation itself. Not a business
/// module - it exists purely to confirm the pipeline (routing, DI, JSON serialization, the
/// standard response envelope) works end to end before any real controller is built on top of it.
/// </summary>
public sealed class SystemController : BaseApiController
{
    /// <summary>Confirms the API is running and returns the standard response envelope.</summary>
    [HttpGet("ping")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    public ActionResult<ApiResponse> Ping()
    {
        return Ok(ApiResponse.Ok("Global Rubber MMM API is running."));
    }
}
