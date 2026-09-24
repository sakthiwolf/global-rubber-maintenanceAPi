namespace GlobalRubber.MMM.Application.Common;

/// <summary>
/// The six actions security.permission_master already has one column per - View maps to
/// can_view, Add maps to can_add (not can_create - the database column is can_add), etc.
/// Central representation so controllers never use magic strings for actions.
/// </summary>
public enum PermissionAction
{
    View,
    Add,
    Edit,
    Delete,
    Approve,
    Export,
}
