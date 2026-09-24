namespace GlobalRubber.MMM.Application.DTOs;

/// <summary>
/// Request body for PUT /api/v1/users/{id} - a full replacement of the editable profile fields plus
/// the row version the caller last read (stale-form protection: if the user changed since, the save is
/// refused with 409). <see cref="NewPassword"/> is optional: the password is changed ONLY when it is
/// supplied and non-empty, and an admin-set password is temporary (mustChangePassword becomes true).
/// Not accepted: userId, userCode, passwordHash, lockout state, audit columns, employeeId/departmentId
/// (see <see cref="CreateUserRequest"/>). No password policy beyond "not empty" (open question Q-37).
/// </summary>
public sealed class UpdateUserRequest
{
    public string LoginId { get; init; } = string.Empty;
    public string UserName { get; init; } = string.Empty;
    public string? Email { get; init; }
    public string? Mobile { get; init; }
    public int RoleId { get; init; }
    public bool IsActive { get; init; } = true;
    public string? NewPassword { get; init; }

    /// <summary>Base64 row_version exactly as returned by UserDto.RowVersion.</summary>
    public string RowVersion { get; init; } = string.Empty;
}
