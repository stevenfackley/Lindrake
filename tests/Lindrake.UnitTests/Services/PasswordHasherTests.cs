using FluentAssertions;
using OmniSift.Api.Services;

namespace OmniSift.UnitTests.Services;

public sealed class PasswordHasherTests
{
    private readonly IPasswordHasher _hasher = new PasswordHasherService();

    [Fact]
    public void Hash_ThenVerifyCorrectPassword_ReturnsTrue()
    {
        var password = "S3cur3P@ssw0rd!";
        var hash = _hasher.Hash(password);

        _hasher.Verify(password, hash).Should().BeTrue();
    }

    [Fact]
    public void Verify_WrongPassword_ReturnsFalse()
    {
        var hash = _hasher.Hash("correct-password");

        _hasher.Verify("wrong-password", hash).Should().BeFalse();
    }

    [Fact]
    public void Hash_SamePasswordTwice_ProducesDifferentHashes()
    {
        var password = "same-password";

        var hash1 = _hasher.Hash(password);
        var hash2 = _hasher.Hash(password);

        hash1.Should().NotBe(hash2, "bcrypt uses random salts");
    }
}
