using System.Net;
using FluentAssertions;
using OmniSift.UnitTests.TestSupport;

namespace OmniSift.UnitTests.Web;

public sealed class AuthHandlerTests
{
    private static readonly DateTimeOffset Now = new(2030, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private static WebClientHarness NewHarness(TimeProvider? time = null) =>
        new(new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Json(HttpStatusCode.OK, "[]")), time);

    [Fact]
    public async Task SignedIn_AddsBearerToken()
    {
        using var harness = NewHarness();
        var token = await harness.SignInAsync();

        await harness.Api.GetDataSourcesAsync();

        var authorization = harness.Handler.Requests.Single().Authorization;
        authorization!.Scheme.Should().Be("Bearer");
        authorization.Parameter.Should().Be(token);
    }

    [Fact]
    public async Task SignedOut_SendsNoAuthorizationHeader()
    {
        using var harness = NewHarness();

        await harness.Api.GetDataSourcesAsync();

        harness.Handler.Requests.Single().Authorization.Should().BeNull();
    }

    [Fact]
    public async Task ExpiredToken_IsNotSent()
    {
        var time = new ManualTimeProvider(Now);
        using var harness = NewHarness(time);
        await harness.SignInAsync();
        time.Advance(TimeSpan.FromHours(2));

        await harness.Api.GetDataSourcesAsync();

        harness.Handler.Requests.Single().Authorization.Should().BeNull();
    }

    [Fact]
    public async Task NoTenantHeader_IsEverSent()
    {
        using var harness = NewHarness();
        await harness.SignInAsync();

        await harness.Api.GetDataSourcesAsync();

        // The tenant travels in the token's tenant_id claim only.
        harness.Handler.Requests.Should().ContainSingle();
        harness.Handler.Requests.Single().HeaderNames.Should().Contain("Authorization").And.NotContain("X-Tenant-Id");
    }
}
