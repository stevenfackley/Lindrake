using System.Buffers.Text;
using System.Text;
using FluentAssertions;
using Lindrake.UnitTests.TestSupport;
using Lindrake.Web.Services;

namespace Lindrake.UnitTests.Web;

public sealed class JwtSessionTests
{
    private static readonly DateTimeOffset Expiry = new(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);
    private static readonly Guid Tenant = Guid.Parse("11111111-2222-3333-4444-555555555555");

    [Fact]
    public void TryRead_ReadsClaimsAndExpiry()
    {
        var session = JwtSession.TryRead(TestJwt.Create(Expiry, Tenant, "ada@example.test"));

        session.Should().NotBeNull();
        session!.ExpiresAt.Should().Be(Expiry);
        session.User.Identity!.IsAuthenticated.Should().BeTrue();
        session.User.Identity.Name.Should().Be("ada@example.test");
        session.User.FindFirst("tenant_id")!.Value.Should().Be(Tenant.ToString());
        session.User.IsInRole("owner").Should().BeTrue();
    }

    [Fact]
    public void TryRead_ArrayAndObjectClaims()
    {
        var session = JwtSession.TryRead(TestJwt.CreateRaw(
            """{"exp":1893456000,"amr":["pwd","otp"],"app_metadata":{"provider":"email"},"email_verified":true}"""));

        session!.User.FindAll("amr").Select(c => c.Value).Should().Equal("pwd", "otp");
        session.User.FindFirst("app_metadata")!.Value.Should().Be("""{"provider":"email"}""");
        session.User.FindFirst("email_verified")!.Value.Should().Be("true");
    }

    [Fact]
    public void TryRead_FractionalExp_IsTruncated()
    {
        JwtSession.TryRead(TestJwt.CreateRaw("""{"exp":1893456000.75}"""))!
            .ExpiresAt.Should().Be(DateTimeOffset.FromUnixTimeSeconds(1893456000));
    }

    [Theory]
    [InlineData(-1, true)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public void IsExpired_ComparesExpWithNow(int secondsUntilExpiry, bool expired)
    {
        var session = JwtSession.TryRead(TestJwt.Create(Expiry, Tenant))!;

        session.IsExpired(Expiry.AddSeconds(-secondsUntilExpiry)).Should().Be(expired);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-jwt")]
    [InlineData("a.b")]
    [InlineData("a.b.c.d")]
    [InlineData("header..signature")]
    [InlineData("header.!!!.signature")]
    public void TryRead_MalformedToken_IsNull(string? token)
    {
        JwtSession.TryRead(token).Should().BeNull();
    }

    [Theory]
    [InlineData("""{"sub":"x"}""")]                 // no exp
    [InlineData("""{"exp":"1893456000"}""")]        // exp not a number
    [InlineData("""{"exp":-5}""")]
    [InlineData("""{"exp":1e300}""")]
    [InlineData("""[1,2]""")]                       // not an object
    public void TryRead_PayloadWithoutUsableExp_IsNull(string payload)
    {
        JwtSession.TryRead(TestJwt.CreateRaw(payload)).Should().BeNull();
    }

    [Fact]
    public void TryRead_PayloadThatIsNotJson_IsNull()
    {
        var notJson = Base64Url.EncodeToString(Encoding.UTF8.GetBytes("hello"));

        JwtSession.TryRead($"eyJhbGciOiJIUzI1NiJ9.{notJson}.sig").Should().BeNull();
    }
}
