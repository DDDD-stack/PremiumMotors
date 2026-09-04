using System.Security.Cryptography;
using System.Text;
using WEBTechnologies_Final.Services;
using Xunit;

namespace PremiumMotors.Tests;

public class PasswordHasherTests
{
    [Fact]
    public void Hash_produces_a_different_value_each_time()
    {
        // Per-user salt: two hashes of the same password must not match.
        Assert.NotEqual(PasswordHasher.Hash("hunter2pass"), PasswordHasher.Hash("hunter2pass"));
    }

    [Fact]
    public void Correct_password_verifies()
    {
        var hash = PasswordHasher.Hash("hunter2pass");
        Assert.Equal(PasswordVerificationResult.Success, PasswordHasher.Verify("hunter2pass", hash));
    }

    [Fact]
    public void Wrong_password_fails()
    {
        var hash = PasswordHasher.Hash("hunter2pass");
        Assert.Equal(PasswordVerificationResult.Failed, PasswordHasher.Verify("hunter2Pass", hash));
    }

    [Fact]
    public void Legacy_plaintext_verifies_but_demands_a_rehash()
    {
        // The old MVC registration path wrote the password straight into PasswordHash.
        Assert.Equal(
            PasswordVerificationResult.SuccessRehashNeeded,
            PasswordHasher.Verify("hunter2pass", "hunter2pass"));
    }

    [Fact]
    public void Legacy_sha256_verifies_but_demands_a_rehash()
    {
        // The old API controller wrote an unsalted SHA-256 hex digest.
        var legacy = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("hunter2pass"))).ToLowerInvariant();

        Assert.Equal(
            PasswordVerificationResult.SuccessRehashNeeded,
            PasswordHasher.Verify("hunter2pass", legacy));
    }

    [Fact]
    public void Legacy_sha256_rejects_the_wrong_password()
    {
        var legacy = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("hunter2pass"))).ToLowerInvariant();
        Assert.Equal(PasswordVerificationResult.Failed, PasswordHasher.Verify("wrong", legacy));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Empty_stored_hash_never_verifies(string? stored)
    {
        Assert.Equal(PasswordVerificationResult.Failed, PasswordHasher.Verify("anything", stored));
    }

    [Fact]
    public void Malformed_pbkdf2_value_fails_rather_than_throwing()
    {
        Assert.Equal(
            PasswordVerificationResult.Failed,
            PasswordHasher.Verify("hunter2pass", "pbkdf2-sha256$notanumber$$$"));
    }
}
