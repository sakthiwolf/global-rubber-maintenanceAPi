namespace GlobalRubber.MMM.Application.Interfaces;

/// <summary>
/// Abstraction over password hashing, so the Application layer never depends on a specific
/// hashing library. No existing hash implementation was found anywhere in the project (zero
/// seeded users, zero references to BCrypt or any other hasher) - see AuthService's remarks
/// for which algorithm the Infrastructure implementation uses and why.
/// </summary>
public interface IPasswordHasher
{
    string HashPassword(string password);

    bool VerifyPassword(string passwordHash, string providedPassword);
}
