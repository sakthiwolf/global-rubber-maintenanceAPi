using GlobalRubber.MMM.Infrastructure.Identity;

namespace GlobalRubber.MMM.Tests;

/// <summary>Tests the real PasswordHasher - it has no DB dependency, so this exercises actual code.</summary>
public class PasswordHasherTests
{
    [Fact]
    public void HashPassword_ThenVerifyPassword_SucceedsForCorrectPassword()
    {
        var hasher = new PasswordHasher();

        var hash = hasher.HashPassword("Correct-Password-1");

        Assert.True(hasher.VerifyPassword(hash, "Correct-Password-1"));
    }

    [Fact]
    public void VerifyPassword_FailsForWrongPassword()
    {
        var hasher = new PasswordHasher();

        var hash = hasher.HashPassword("Correct-Password-1");

        Assert.False(hasher.VerifyPassword(hash, "Wrong-Password"));
    }

    [Fact]
    public void HashPassword_ProducesDifferentHashes_ForTheSamePassword()
    {
        var hasher = new PasswordHasher();

        var hash1 = hasher.HashPassword("Same-Password-1");
        var hash2 = hasher.HashPassword("Same-Password-1");

        // Different salt per call - hashes must differ even for an identical input password.
        Assert.NotEqual(hash1, hash2);
        Assert.True(hasher.VerifyPassword(hash1, "Same-Password-1"));
        Assert.True(hasher.VerifyPassword(hash2, "Same-Password-1"));
    }

    [Fact]
    public void HashPassword_NeverProducesThePlaintextPassword()
    {
        var hasher = new PasswordHasher();

        var hash = hasher.HashPassword("Correct-Password-1");

        Assert.DoesNotContain("Correct-Password-1", hash);
    }
}
