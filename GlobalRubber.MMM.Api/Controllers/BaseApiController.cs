using Microsoft.AspNetCore.Mvc;

namespace GlobalRubber.MMM.Api.Controllers;

/// <summary>
/// Base type for every API controller. Fixes the route convention
/// (<c>api/v1/[controller]</c>) so each module only names its controller once, and centralises
/// the plumbing shared by every action.
///
/// Deliberately contains no business logic, no data access and no response-building helpers
/// beyond routing/versioning - those belong in the Application layer and are returned by
/// controller actions as <c>ApiResponse</c>/<c>ApiResponse&lt;T&gt;</c> directly.
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
public abstract class BaseApiController : ControllerBase
{
}
