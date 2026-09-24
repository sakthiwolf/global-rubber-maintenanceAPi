using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Domain.Entities;
using Microsoft.AspNetCore.Identity;

namespace GlobalRubber.MMM.Infrastructure.Identity;

/// <summary>
/// Wraps ASP.NET Core Identity's <see cref="PasswordHasher{TUser}"/> (PBKDF2, salted,
/// versioned format). No existing hashing implementation or stored hash exists anywhere in the
/// project to preserve - the database has zero seeded users (see docs/GlobalRubber_Database_
/// Design.md section 14, "Initial Administrator") - so this is a fresh choice, not a change to
/// an existing algorithm. PBKDF2 via the built-in hasher was chosen over adding a BCrypt
/// dependency because it requires no new third-party package and matches the algorithm already
/// named in docs/GlobalRubber_MMM_System_Analysis.md section 17.5.
/// </summary>
public sealed class PasswordHasher : IPasswordHasher
{
    private readonly PasswordHasher<User> _inner = new();

    public string HashPassword(string password) =>
        _inner.HashPassword(user: null!, password);

    public bool VerifyPassword(string passwordHash, string providedPassword)
    {
        var result = _inner.VerifyHashedPassword(user: null!, passwordHash, providedPassword);

        return result is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded;
    }
}
