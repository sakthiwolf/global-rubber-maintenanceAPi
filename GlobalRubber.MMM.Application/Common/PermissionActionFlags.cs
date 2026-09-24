namespace GlobalRubber.MMM.Application.Common;

/// <summary>
/// The six action flags for one (role, module) permission_master row. A null instance (see
/// IPermissionRepository.GetPermissionFlagsAsync) means no row exists - "missing row means no
/// access", the same rule Step 6's permission matrix already established.
/// </summary>
public sealed record PermissionActionFlags(
    bool CanView, bool CanAdd, bool CanEdit, bool CanDelete, bool CanApprove, bool CanExport)
{
    public bool Has(PermissionAction action) => action switch
    {
        PermissionAction.View => CanView,
        PermissionAction.Add => CanAdd,
        PermissionAction.Edit => CanEdit,
        PermissionAction.Delete => CanDelete,
        PermissionAction.Approve => CanApprove,
        PermissionAction.Export => CanExport,
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown permission action."),
    };
}
