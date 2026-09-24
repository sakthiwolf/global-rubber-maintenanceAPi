namespace GlobalRubber.MMM.Application.DTOs;

/// <summary>
/// User information returned to API callers. Deliberately excludes PasswordHash,
/// FailedLoginCount and LockoutEndAt - none of that internal security state is
/// appropriate for a general-purpose user list/detail response.
/// </summary>
public sealed class UserDto
{
    public int UserId { get; init; }
    public string UserCode { get; init; } = string.Empty;
    public string LoginId { get; init; } = string.Empty;
    public string UserName { get; init; } = string.Empty;
    public int RoleId { get; init; }
    public string RoleName { get; init; } = string.Empty;
    public int? EmployeeId { get; init; }
    public int? DepartmentId { get; init; }
    public string? Email { get; init; }
    public string? Mobile { get; init; }
    public bool MustChangePassword { get; init; }
    public DateTime? LastLoginAt { get; init; }
    public bool IsActive { get; init; }

    /// <summary>
    /// Base64 row_version. The client keeps it and sends it back on PUT: if the user changed in between,
    /// the update is refused with 409. Opaque - never interpreted client-side.
    /// </summary>
    public string RowVersion { get; init; } = string.Empty;
}
