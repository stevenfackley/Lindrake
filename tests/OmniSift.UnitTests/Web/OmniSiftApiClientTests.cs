using System.Net;
using FluentAssertions;
using OmniSift.Shared.DTOs;
using OmniSift.UnitTests.TestSupport;
using OmniSift.Web.Services;

namespace OmniSift.UnitTests.Web;

public sealed class OmniSiftApiClientTests
{
    private static readonly Guid SourceId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    [Fact]
    public async Task Success_DeserializesWithTheSourceGeneratedContext()
    {
        using var harness = new WebClientHarness(new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Json(
            HttpStatusCode.OK,
            $$"""{"id":"{{SourceId}}","sourceType":"pdf","status":"completed","metadata":{"pages":3,"author":"x"},"chunkCount":7}""")));

        var result = await harness.Api.GetDataSourceAsync(SourceId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(SourceId);
        result.Value.Status.Should().Be(IngestionStatuses.Completed);
        result.Value.ChunkCount.Should().Be(7);
        result.Value.Metadata.Should().ContainKeys("pages", "author");
        harness.Handler.Requests.Should().ContainSingle().Which.PathAndQuery.Should().Be($"/api/datasources/{SourceId}");
    }

    [Fact]
    public async Task RequestBody_IsCamelCaseJson()
    {
        using var harness = new WebClientHarness(new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Json(
            HttpStatusCode.OK, $$"""{"dataSourceId":"{{SourceId}}","status":"completed","message":"ok"}""")));

        await harness.Api.IngestWebAsync("https://example.test/page");

        var request = harness.Handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Post);
        request.ContentType.Should().Be("application/json");
        request.Body.Should().Be("""{"url":"https://example.test/page"}""");
    }

    [Fact]
    public async Task ErrorResponse_CarriesTheProblemDetailsMessage()
    {
        using var harness = new WebClientHarness(new FakeHttpMessageHandler(_ =>
            FakeHttpMessageHandler.Problem(HttpStatusCode.Conflict, "An account with this email address already exists.")));

        var result = await harness.Api.RegisterAsync(new RegisterRequest { Email = "a@b.test", Password = "password1", TenantName = "Acme" });

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(new ApiError(409, "An account with this email address already exists."));
        var value = () => result.Value;
        value.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task Unauthorized_WhileSignedIn_SignsOut()
    {
        using var harness = new WebClientHarness(new FakeHttpMessageHandler(_ =>
            FakeHttpMessageHandler.Problem(HttpStatusCode.Unauthorized, title: "Unauthorized")));
        await harness.SignInAsync();
        var changes = 0;
        harness.Auth.OnAuthChanged += () => changes++;

        var result = await harness.Api.GetDataSourcesAsync();

        result.Error!.IsUnauthorized.Should().BeTrue();
        harness.Auth.IsAuthenticated.Should().BeFalse();
        harness.Js.LocalStorage.Should().NotContainKey("omnisift_token");
        changes.Should().Be(1);
    }

    [Fact]
    public async Task Unauthorized_WhileSignedOut_DoesNotRaiseAuthChanged()
    {
        // A failed login is a 401 too: nothing to sign out of.
        using var harness = new WebClientHarness(new FakeHttpMessageHandler(_ =>
            FakeHttpMessageHandler.Problem(HttpStatusCode.Unauthorized, "Invalid credentials.")));
        var changes = 0;
        harness.Auth.OnAuthChanged += () => changes++;

        var result = await harness.Api.LoginAsync(new LoginRequest { Email = "a@b.test", Password = "x", TenantId = Guid.NewGuid() });

        result.Error.Should().Be(new ApiError(401, "Invalid credentials."));
        changes.Should().Be(0);
    }

    [Fact]
    public async Task NetworkFailure_IsAnErrorWithStatusZero()
    {
        using var harness = new WebClientHarness(new FakeHttpMessageHandler(_ => throw new HttpRequestException("connection refused")));

        var result = await harness.Api.GetDataSourcesAsync();

        result.Error!.StatusCode.Should().Be(0);
        result.Error.IsTransient.Should().BeTrue();
        result.Error.Message.Should().Contain("Could not reach");
    }

    [Fact]
    public async Task UnreadableSuccessBody_IsAnError()
    {
        using var harness = new WebClientHarness(new FakeHttpMessageHandler(_ =>
            FakeHttpMessageHandler.Json(HttpStatusCode.OK, "<html>not json</html>", "text/html")));

        var result = await harness.Api.GetDataSourcesAsync();

        result.Error.Should().Be(ApiError.InvalidResponse(200));
    }

    [Fact]
    public async Task Delete_NoContent_IsSuccess()
    {
        using var harness = new WebClientHarness(new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent)));

        var result = await harness.Api.DeleteDataSourceAsync(SourceId);

        result.IsSuccess.Should().BeTrue();
        harness.Handler.Requests.Single().Method.Should().Be(HttpMethod.Delete);
    }

    [Fact]
    public async Task CallerCancellation_Throws()
    {
        using var harness = new WebClientHarness(new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => harness.Api.GetDataSourcesAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
