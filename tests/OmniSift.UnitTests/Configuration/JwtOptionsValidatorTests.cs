// ============================================================
// Unit Tests — JwtOptionsValidator
// The HS256 identity provider is development/testing-only and
// needs a 256-bit signing secret.
// ============================================================

using FluentAssertions;
using Microsoft.Extensions.Hosting;
using Moq;
using OmniSift.Api.Options;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace OmniSift.UnitTests.Configuration;

public sealed class JwtOptionsValidatorTests
{
    private const string StrongSecret = "0123456789abcdef0123456789abcdef"; // 32 bytes

    private static JwtOptionsValidator CreateValidator(string environment, string supabaseUrl = "")
    {
        var env = new Mock<IHostEnvironment>();
        env.SetupGet(e => e.EnvironmentName).Returns(environment);
        return new JwtOptionsValidator(MsOptions.Create(new SupabaseOptions { Url = supabaseUrl }), env.Object);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void Hs256_OutsideDevelopment_Fails(string environment)
    {
        var result = CreateValidator(environment).Validate(null, new JwtOptions { Secret = StrongSecret });

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Supabase:Url");
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
    [InlineData("")]
    [InlineData("0123456789abcdef0123456789abcde")] // 31 bytes
    public void Hs256_WithShortSecret_Fails(string secret)
    {
        var result = CreateValidator("Development").Validate(null, new JwtOptions { Secret = secret });

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Jwt:Secret");
    }

    [Fact]
    public void Supabase_InProduction_IgnoresJwtSecret()
    {
        var result = CreateValidator("Production", supabaseUrl: "https://abcd.supabase.co")
            .Validate(null, new JwtOptions { Secret = string.Empty });

        result.Succeeded.Should().BeTrue();
    }
}
