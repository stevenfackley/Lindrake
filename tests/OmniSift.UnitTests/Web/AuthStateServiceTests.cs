using System.Net;
using FluentAssertions;
using OmniSift.UnitTests.TestSupport;

namespace OmniSift.UnitTests.Web;

public sealed class AuthStateServiceTests
{
    private static readonly DateTimeOffset Now = new(2030, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private static WebClientHarness NewHarness(ManualTimeProvider time) =>
        new(new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)), time);

    [Fact]
    public async Task SignIn_ValidToken_IsAuthenticatedWithClaims()
    {
        var time = new ManualTimeProvider(Now);
        using var harness = NewHarness(time);

        var token = TestJwt.Create(Now.AddMinutes(60), WebClientHarness.TenantId);
        (await harness.Auth.SignInAsync(token, WebClientHarness.TenantId)).Should().BeTrue();

        harness.Auth.IsAuthenticated.Should().BeTrue();
        harness.Auth.Token.Should().Be(token);
        harness.Auth.ExpiresAt.Should().Be(Now.AddMinutes(60));
        harness.Auth.User.FindFirst("tenant_id")!.Value.Should().Be(WebClientHarness.TenantId.ToString());
        harness.Js.LocalStorage["omnisift_token"].Should().Be(token);
        harness.Js.LocalStorage["omnisift_last_tenant"].Should().Be(WebClientHarness.TenantId.ToString());
    }

    [Fact]
    public async Task PastExp_CountsAsSignedOut_AndRaisesAuthChanged()
    {
        var time = new ManualTimeProvider(Now);
        using var harness = NewHarness(time);
        await harness.Auth.SignInAsync(TestJwt.Create(Now.AddMinutes(60), WebClientHarness.TenantId), WebClientHarness.TenantId);
        var changes = 0;
        harness.Auth.OnAuthChanged += () => changes++;

        time.Advance(TimeSpan.FromMinutes(59));
        harness.Auth.IsAuthenticated.Should().BeTrue();
        changes.Should().Be(0);

        time.Advance(TimeSpan.FromMinutes(1));
        harness.Auth.IsAuthenticated.Should().BeFalse();
        harness.Auth.Token.Should().BeNull();
        harness.Auth.User.Identity!.IsAuthenticated.Should().BeFalse();
        changes.Should().Be(1, "the expiry timer tells routing to redirect to the login page");
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData(null)]
    public async Task SignIn_UnreadableOrExpiredToken_IsRefused(string? token)
    {
        var time = new ManualTimeProvider(Now);
        using var harness = NewHarness(time);
        token ??= TestJwt.Create(Now.AddSeconds(-1), WebClientHarness.TenantId);

        (await harness.Auth.SignInAsync(token, WebClientHarness.TenantId)).Should().BeFalse();

        harness.Auth.IsAuthenticated.Should().BeFalse();
        harness.Js.LocalStorage.Should().BeEmpty();
    }

    [Fact]
    public async Task Initialize_RestoresAValidStoredToken()
    {
        var time = new ManualTimeProvider(Now);
        using var harness = NewHarness(time);
        var token = TestJwt.Create(Now.AddMinutes(5), WebClientHarness.TenantId);
        harness.Js.LocalStorage["omnisift_token"] = token;

        await harness.Auth.InitializeAsync();

        harness.Auth.Token.Should().Be(token);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Initialize_DiscardsAnExpiredOrUnreadableStoredToken(bool expired)
    {
        var time = new ManualTimeProvider(Now);
        using var harness = NewHarness(time);
        harness.Js.LocalStorage["omnisift_token"] = expired
            ? TestJwt.Create(Now.AddMinutes(-5), WebClientHarness.TenantId)
            : "not.a.jwt";

        await harness.Auth.InitializeAsync();

        harness.Auth.IsAuthenticated.Should().BeFalse();
        harness.Js.LocalStorage.Should().NotContainKey("omnisift_token");
    }

    [Fact]
    public async Task SignOut_ClearsTheToken_ButKeepsTheLastTenant()
    {
        var time = new ManualTimeProvider(Now);
        using var harness = NewHarness(time);
        await harness.SignInAsync();

        await harness.Auth.SignOutAsync();

        harness.Auth.IsAuthenticated.Should().BeFalse();
        harness.Js.LocalStorage.Should().NotContainKey("omnisift_token");
        (await harness.Auth.GetLastTenantIdAsync()).Should().Be(WebClientHarness.TenantId);
    }

    [Fact]
    public async Task SignOut_WhenSignedOut_RaisesNothing()
    {
        using var harness = NewHarness(new ManualTimeProvider(Now));
        var changes = 0;
        harness.Auth.OnAuthChanged += () => changes++;

        await harness.Auth.SignOutAsync();

        changes.Should().Be(0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task LastTenant_MissingOrInvalid_IsNull(string? stored)
    {
        using var harness = NewHarness(new ManualTimeProvider(Now));
        if (stored is not null)
            harness.Js.LocalStorage["omnisift_last_tenant"] = stored;

        (await harness.Auth.GetLastTenantIdAsync()).Should().BeNull();
    }
}
