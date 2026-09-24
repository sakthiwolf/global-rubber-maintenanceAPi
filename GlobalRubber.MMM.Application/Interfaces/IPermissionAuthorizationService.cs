using GlobalRubber.MMM.Application.Common;

namespace GlobalRubber.MMM.Application.Interfaces;

public interface IPermissionAuthorizationService
{
    /// <summary>
    /// True only if security.permission_master actually grants the given action, for the given
    /// role, on the given module - the JWT's role claim alone is never sufficient. A role with
    /// no permission_master row for that module returns false ("missing row means no access").
    /// </summary>
    Task<bool> HasPermissionAsync(
        string roleCode, string moduleCode, PermissionAction action, CancellationToken cancellationToken);
}
