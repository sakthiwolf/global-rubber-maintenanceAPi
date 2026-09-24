using System.Security.Claims;
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
    // The JWT "sub" claim is the user id; the default inbound claim mapping surfaces it as
    // ClaimTypes.NameIdentifier (the same lookup GlobalExceptionHandler uses). Used by the write endpoints
    // for UpdatedBy/CreatedBy and the audit entry.
    protected int? GetAuthenticatedUserId() =>
        int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId) ? userId : null;
}
