namespace GlobalRubber.MMM.Application.DTOs;

/// <summary>
/// Request body for POST /api/v1/users. Only fields the existing user model supports and the
/// caller may legitimately choose. Deliberately absent (a client that sends them has them ignored by
/// model binding): userId, userCode (issued from the USER document sequence), passwordHash (computed
/// from <see cref="Password"/>), mustChangePassword (always true for an admin-set password - the
/// analysis calls for a temporary password that must be changed), failed-login/lockout state, audit
/// columns and rowVersion. employeeId/departmentId are not accepted yet: the employee and department
/// masters have no backend, so there is nothing valid to reference.
///
/// No password policy is applied beyond "not empty" - the policy is still an open question (Q-37).
/// </summary>
public sealed class CreateUserRequest
{
    public string LoginId { get; init; } = string.Empty;
    public string UserName { get; init; } = string.Empty;
    public string? Email { get; init; }
    public string? Mobile { get; init; }
    public int RoleId { get; init; }
    public string Password { get; init; } = string.Empty;
    public bool IsActive { get; init; } = true;
}
