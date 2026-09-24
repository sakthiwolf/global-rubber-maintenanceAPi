using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Application.Interfaces.Repositories;

namespace GlobalRubber.MMM.Application.Services;

/// <summary>
/// Deliberately not cached, per this step's instructions - a database-backed lookup is enough
/// for now, and caching would risk serving a stale permission after Step 6 changes one.
/// </summary>
public sealed class PermissionAuthorizationService : IPermissionAuthorizationService
{
    private readonly IPermissionRepository _permissionRepository;

    public PermissionAuthorizationService(IPermissionRepository permissionRepository)
    {
        _permissionRepository = permissionRepository;
    }

    public async Task<bool> HasPermissionAsync(
        string roleCode, string moduleCode, PermissionAction action, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(roleCode))
        {
            return false;
        }

        var flags = await _permissionRepository.GetPermissionFlagsAsync(roleCode, moduleCode, cancellationToken);

        return flags?.Has(action) ?? false;
    }
}
