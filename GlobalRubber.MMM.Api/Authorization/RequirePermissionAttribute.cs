using System.Security.Claims;
using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.Interfaces;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace GlobalRubber.MMM.Api.Authorization;

/// <summary>
/// Reusable, declarative endpoint authorization:
/// <c>[RequirePermission(ModuleCodes.AdminUsers, PermissionAction.View)]</c>.
///
/// A plain MVC authorization filter (IAsyncAuthorizationFilter) rather than ASP.NET Core's
/// full policy-based system (IAuthorizationHandler/IAuthorizationRequirement/
/// IAuthorizationPolicyProvider) - that machinery exists to support named, pre-registered
/// policies; here every (module, action) pair needs its own dynamic check, and a policy
/// provider capable of synthesizing a policy per attribute instance is considerably more
/// moving parts for the same outcome. This keeps the "do not build a complex authorization
/// framework" instruction.
///
/// Self-sufficient for both 401 and 403: UseAuthentication() always populates HttpContext.User
/// from the bearer token if one was supplied, regardless of whether [Authorize] is also
/// present, so this attribute alone is enough - no separate [Authorize] needed on top of it.
/// Both failures are raised as the project's existing UnauthorizedAccessException/
/// ForbiddenAccessException, which GlobalExceptionHandler already maps to 401/403 in the
/// standard ApiResponse shape - no new response format, no new exception type.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public sealed class RequirePermissionAttribute : Attribute, IAsyncAuthorizationFilter
{
    private readonly string _moduleCode;
    private readonly PermissionAction _action;

    public RequirePermissionAttribute(string moduleCode, PermissionAction action)
    {
        _moduleCode = moduleCode;
        _action = action;
    }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.User;

        if (user.Identity is not { IsAuthenticated: true })
        {
            throw new UnauthorizedAccessException();
        }

        // The JWT role claim only identifies *which* role the user has - it is never treated
        // as sufficient on its own. The actual grant is looked up in permission_master below.
        var roleCode = user.FindFirst(ClaimTypes.Role)?.Value;

        var authorizationService = context.HttpContext.RequestServices
            .GetRequiredService<IPermissionAuthorizationService>();

        var allowed = !string.IsNullOrEmpty(roleCode)
            && await authorizationService.HasPermissionAsync(
                roleCode, _moduleCode, _action, context.HttpContext.RequestAborted);

        if (!allowed)
        {
            throw new ForbiddenAccessException();
        }
    }
}
