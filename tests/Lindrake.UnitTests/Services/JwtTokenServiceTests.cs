using System.IdentityModel.Tokens.Jwt;
using FluentAssertions;
using Lindrake.Api.Models;
using Lindrake.Api.Options;
using Lindrake.Api.Services;
using Microsoft.Extensions.Options;

namespace Lindrake.UnitTests.Services;

public sealed class JwtTokenServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 9, 30, 0, TimeSpan.Zero);

    private static JwtTokenService BuildService(string secret = "test-secret-key-must-be-at-least-32-ch!")
    {
        var opts = Options.Create(new JwtOptions
        {
            Secret = secret,
            Issuer = "TestIssuer",
            Audience = "TestAudience",
            ExpiryMinutes = 30
        });
        return new JwtTokenService(opts, new FixedTimeProvider(Now));
    }

    [Fact]
    public void CreateToken_ContainsExpectedClaims()
    {
        var svc = BuildService();
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        var user = new AppUser
        {
            Id = userId,
            TenantId = tenantId,
            Email = "test@example.com",
            Role = "owner"
        };

        var (tokenString, expiresAt) = svc.CreateToken(user);

        tokenString.Should().NotBeNullOrWhiteSpace();

        var handler = new JwtSecurityTokenHandler();
        handler.CanReadToken(tokenString).Should().BeTrue();

        var jwt = handler.ReadJwtToken(tokenString);

        jwt.Subject.Should().Be(userId.ToString());
        jwt.Claims.Should().Contain(c => c.Type == "tenant_id" && c.Value == tenantId.ToString());
        jwt.Claims.Should().Contain(c => c.Type == JwtRegisteredClaimNames.Email && c.Value == "test@example.com");
        jwt.Claims.Should().Contain(c => c.Type == "role" && c.Value == "owner");
        jwt.Issuer.Should().Be("TestIssuer");
    }

    [Fact]
    public void CreateToken_ExpiresExpiryMinutesAfterTheClock()
    {
        var (tokenString, expiresAt) = BuildService().CreateToken(
            new AppUser { Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), Email = "a@example.com", Role = "owner" });

        var expected = Now.UtcDateTime.AddMinutes(30);
        expiresAt.Should().Be(expected);
        expiresAt.Kind.Should().Be(DateTimeKind.Utc);
        new JwtSecurityTokenHandler().ReadJwtToken(tokenString).ValidTo.Should().Be(expected);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
