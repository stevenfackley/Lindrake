// ============================================================
// Unit Tests — JwtOptionsValidator
// The HS256 identity provider is development/testing-only unless
// explicitly enabled, and always needs a strong, private secret.
// ============================================================

using System.Text.Json;
using FluentAssertions;
using Lindrake.Api.Options;
using Microsoft.Extensions.Hosting;
using Moq;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Lindrake.UnitTests.Configuration;

public sealed class JwtOptionsValidatorTests
{
    private const string StrongSecret = "0123456789abcdef0123456789abcdef"; // 32 bytes
    private const string OptInFlag = "Jwt:AllowLocalIdentityProvider";

    private static JwtOptionsValidator CreateValidator(string environment, string supabaseUrl = "")
    {
        var env = new Mock<IHostEnvironment>();
        env.SetupGet(e => e.EnvironmentName).Returns(environment);
        return new JwtOptionsValidator(MsOptions.Create(new SupabaseOptions { Url = supabaseUrl }), env.Object);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void Hs256_OutsideDevelopment_WithoutOptIn_Fails(string environment)
    {
        var result = CreateValidator(environment).Validate(null, new JwtOptions { Secret = StrongSecret });

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Supabase:Url").And.Contain(OptInFlag).And.Contain(environment);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void Hs256_OutsideDevelopment_WithOptInAndStrongSecret_Succeeds(string environment)
    {
        var result = CreateValidator(environment).Validate(
            null, new JwtOptions { Secret = StrongSecret, AllowLocalIdentityProvider = true });

        result.Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("0123456789abcdef0123456789abcde")] // 31 bytes
    public void Hs256_OutsideDevelopment_WithOptInAndShortSecret_Fails(string secret)
    {
        var result = CreateValidator("Production").Validate(
            null, new JwtOptions { Secret = secret, AllowLocalIdentityProvider = true });

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Jwt:Secret").And.Contain(OptInFlag);
    }

    [Fact]
    public void Hs256_OutsideDevelopment_WithOptInAndTheDevelopmentDefaultSecret_Fails()
    {
        var result = CreateValidator("Production").Validate(
            null,
            new JwtOptions { Secret = JwtOptionsValidator.DevelopmentDefaultSecret, AllowLocalIdentityProvider = true });

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("development default").And.Contain(OptInFlag);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public void Hs256_InDevelopmentOrTesting_WithStrongSecret_Succeeds(string environment)
    {
        var result = CreateValidator(environment).Validate(null, new JwtOptions { Secret = StrongSecret });

        result.Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public void Hs256_InDevelopmentOrTesting_AcceptsTheDevelopmentDefaultSecret(string environment)
    {
        var result = CreateValidator(environment).Validate(
            null, new JwtOptions { Secret = JwtOptionsValidator.DevelopmentDefaultSecret });

        result.Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("0123456789abcdef0123456789abcde")] // 31 bytes
    public void Hs256_WithShortSecret_Fails(string secret)
    {
        var result = CreateValidator("Development").Validate(null, new JwtOptions { Secret = secret });

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Jwt:Secret");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Supabase_InProduction_IgnoresJwtSecretAndOptIn(bool optIn)
    {
        var result = CreateValidator("Production", supabaseUrl: "https://abcd.supabase.co")
            .Validate(null, new JwtOptions { Secret = string.Empty, AllowLocalIdentityProvider = optIn });

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void OptIn_IsOffByDefault()
    {
        new JwtOptions().AllowLocalIdentityProvider.Should().BeFalse();
    }

    [Fact]
    public void DevelopmentDefaultSecret_MatchesAppsettingsDevelopment()
    {
        var path = Path.Combine(RepoRoot(), "src", "Lindrake.Api", "appsettings.Development.json");
        using var json = JsonDocument.Parse(File.ReadAllText(path));

        json.RootElement.GetProperty("Jwt").GetProperty("Secret").GetString()
            .Should().Be(JwtOptionsValidator.DevelopmentDefaultSecret);
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Lindrake.sln")))
                return dir.FullName;
        }

        throw new InvalidOperationException("Could not locate Lindrake.sln above " + AppContext.BaseDirectory);
    }
}
