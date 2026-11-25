using System.Net;
using FluentAssertions;
using Lindrake.Shared.DTOs;
using Lindrake.UnitTests.TestSupport;
using Lindrake.Web.ViewModels;
using static Lindrake.UnitTests.TestSupport.FakeHttpMessageHandler;

namespace Lindrake.UnitTests.Web;

public sealed class DataSourcesViewModelTests
{
    private static readonly Guid UploadId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly PollingOptions FastPolling = new(TimeSpan.FromMilliseconds(5), TimeSpan.FromSeconds(10));
    private static readonly DateTimeOffset Now = new(2030, 6, 1, 12, 0, 0, TimeSpan.Zero);

    // Real time, and only a hang guard: a passing run never waits on it. Polling itself
    // runs on the ManualTimeProvider each test hands to the view model.
    private static readonly TimeSpan HangGuard = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Completes once the view model is asleep between polls: its cap timer and its interval
    /// timer are both pending on the fake clock. Moving the clock any earlier would race it.
    /// </summary>
    private static Task AsleepAsync(ManualTimeProvider time) => time.WhenTimersPendingAsync(2).WaitAsync(HangGuard);

    /// <summary>Lets <paramref name="by"/> pass on the fake clock once the view model is asleep.</summary>
    private static async Task ElapseAsync(ManualTimeProvider time, TimeSpan by)
    {
        await AsleepAsync(time);
        time.Advance(by);
    }

    private static Task IdleAsync(DataSourcesViewModel vm) => vm.WhenPollingIdleAsync().WaitAsync(HangGuard);

    private static string Source(Guid id, string status, int chunks = 0, string? error = null) =>
        $$"""{"id":"{{id}}","sourceType":"pdf","fileName":"report.pdf","status":"{{status}}","chunkCount":{{chunks}}{{(error is null ? "" : $",\"errorMessage\":\"{error}\"")}}}""";

    private static string List(params string[] sources) => "[" + string.Join(",", sources) + "]";

    private static UploadFile Pdf(long size = 1024) =>
        new("report.pdf", size, ContentType: null, () => new MemoryStream([1, 2, 3]));

    private static HttpResponseMessage Accepted() => Json(
        HttpStatusCode.Accepted,
        $$"""{"dataSourceId":"{{UploadId}}","status":"pending","message":"File accepted."}""");

    [Fact]
    public async Task Load_RequestsTheFirstPageExplicitly()
    {
        using var harness = new WebClientHarness(new FakeHttpMessageHandler(_ => Json(HttpStatusCode.OK, List(Source(UploadId, "completed")))));
        using var vm = new DataSourcesViewModel(harness.Api, FastPolling);
        var changes = 0;
        vm.Changed += () => changes++;

        await vm.LoadAsync();

        harness.Handler.Requests.Single().PathAndQuery.Should().Be("/api/datasources?skip=0&take=200");
        vm.Items.Should().ContainSingle().Which.Id.Should().Be(UploadId);
        vm.IsLoading.Should().BeFalse();
        vm.HasMore.Should().BeFalse();
        changes.Should().BeGreaterThanOrEqualTo(2, "the page re-renders when loading starts and ends");
    }

    [Fact]
    public async Task LoadMore_AfterAFullPage_RequestsTheNextPage()
    {
        var fullPage = List([.. Enumerable.Range(0, DataSourcesViewModel.PageSize).Select(_ => Source(Guid.NewGuid(), "completed"))]);
        using var harness = new WebClientHarness(Sequence(
            () => Json(HttpStatusCode.OK, fullPage),
            () => Json(HttpStatusCode.OK, List(Source(UploadId, "completed")))));
        using var vm = new DataSourcesViewModel(harness.Api, FastPolling);

        await vm.LoadAsync();
        vm.HasMore.Should().BeTrue();
        await vm.LoadMoreAsync();

        harness.Handler.Requests.Last().PathAndQuery.Should().Be("/api/datasources?skip=200&take=200");
        vm.Items.Should().HaveCount(DataSourcesViewModel.PageSize + 1);
        vm.HasMore.Should().BeFalse();
    }

    [Fact]
    public async Task Load_Error_IsShownAndLoadingEnds()
    {
        using var harness = new WebClientHarness(new FakeHttpMessageHandler(_ => Problem(HttpStatusCode.InternalServerError, "Database unavailable.")));
        using var vm = new DataSourcesViewModel(harness.Api, FastPolling);

        await vm.LoadAsync();

        vm.LoadError.Should().Be("Failed to load data sources: Database unavailable.");
        vm.IsLoading.Should().BeFalse();
    }

    [Fact]
    public async Task Upload_SendsTheRealMimeTypeAndSourceType_ThenPollsUntilCompleted()
    {
        var itemPolls = 0;
        using var harness = new WebClientHarness(new FakeHttpMessageHandler(request => (request.Method.Method, request.PathAndQuery) switch
        {
            ("POST", "/api/datasources/upload") => Accepted(),
            ("GET", var path) when path.StartsWith("/api/datasources?", StringComparison.Ordinal) =>
                Json(HttpStatusCode.OK, List(Source(UploadId, "pending"))),
            ("GET", var path) when path == $"/api/datasources/{UploadId}" => Interlocked.Increment(ref itemPolls) switch
            {
                1 => Json(HttpStatusCode.OK, Source(UploadId, "processing")),
                _ => Json(HttpStatusCode.OK, Source(UploadId, "completed", chunks: 12))
            },
            _ => throw new InvalidOperationException($"Unexpected {request.Method} {request.PathAndQuery}")
        }));
        var time = new ManualTimeProvider(Now);
        using var vm = new DataSourcesViewModel(harness.Api, FastPolling, time);

        (await vm.UploadAsync(Pdf(), sourceType: "pdf")).Should().BeTrue();
        vm.IsSubmitting.Should().BeFalse();
        await ElapseAsync(time, FastPolling.Interval);
        await ElapseAsync(time, FastPolling.Interval);
        await IdleAsync(vm);

        var upload = harness.Handler.Requests.First(r => r.PathAndQuery == "/api/datasources/upload");
        upload.Body.Should().Contain("Content-Type: application/pdf")
            .And.Contain("name=sourceType").And.Contain("filename=report.pdf");
        itemPolls.Should().Be(2);
        vm.Items.Should().ContainSingle().Which.Status.Should().Be(IngestionStatuses.Completed);
        vm.SubmitStatus.Should().Be(StatusMessage.Success("report.pdf ingested (12 chunks)."));
    }

    [Fact]
    public async Task Upload_IngestionFails_ShowsTheError()
    {
        using var harness = new WebClientHarness(new FakeHttpMessageHandler(request => request.PathAndQuery switch
        {
            "/api/datasources/upload" => Accepted(),
            var path when path.StartsWith("/api/datasources?", StringComparison.Ordinal) => Json(HttpStatusCode.OK, "[]"),
            _ => Json(HttpStatusCode.OK, Source(UploadId, "failed", error: "No text found."))
        }));
        var time = new ManualTimeProvider(Now);
        using var vm = new DataSourcesViewModel(harness.Api, FastPolling, time);

        await vm.UploadAsync(Pdf());
        await ElapseAsync(time, FastPolling.Interval);
        await IdleAsync(vm);

        vm.SubmitStatus.Should().Be(StatusMessage.Error("report.pdf failed: No text found."));
    }

    [Fact]
    public async Task Polling_StopsAtTheCap()
    {
        using var harness = new WebClientHarness(new FakeHttpMessageHandler(request => request.PathAndQuery switch
        {
            "/api/datasources/upload" => Accepted(),
            var path when path.StartsWith("/api/datasources?", StringComparison.Ordinal) => Json(HttpStatusCode.OK, "[]"),
            _ => Json(HttpStatusCode.OK, Source(UploadId, "processing"))
        }));
        // A 5 s cap over a 2 s interval: polls at 2 s and 4 s, and the cap falls a second
        // before the third, so the clock never has the cap and a poll due at the same instant.
        var time = new ManualTimeProvider(Now);
        var polling = new PollingOptions(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5));
        using var vm = new DataSourcesViewModel(harness.Api, polling, time);

        await vm.UploadAsync(Pdf());
        await ElapseAsync(time, polling.Interval);
        await ElapseAsync(time, polling.Interval);
        await ElapseAsync(time, TimeSpan.FromSeconds(1));
        await IdleAsync(vm);

        vm.SubmitStatus!.Kind.Should().Be(AlertKind.Info);
        vm.SubmitStatus.Text.Should().Contain("still being ingested");
        harness.Handler.Requests.Count(r => r.PathAndQuery == $"/api/datasources/{UploadId}").Should().Be(2);
    }

    [Fact]
    public async Task Polling_TransientErrors_AreRetried()
    {
        var itemPolls = 0;
        using var harness = new WebClientHarness(new FakeHttpMessageHandler(request => request.PathAndQuery switch
        {
            "/api/datasources/upload" => Accepted(),
            var path when path.StartsWith("/api/datasources?", StringComparison.Ordinal) => Json(HttpStatusCode.OK, "[]"),
            _ => Interlocked.Increment(ref itemPolls) == 1
                ? Problem(HttpStatusCode.ServiceUnavailable)
                : Json(HttpStatusCode.OK, Source(UploadId, "completed", chunks: 1))
        }));
        var time = new ManualTimeProvider(Now);
        using var vm = new DataSourcesViewModel(harness.Api, FastPolling, time);

        await vm.UploadAsync(Pdf());
        await ElapseAsync(time, FastPolling.Interval);
        await ElapseAsync(time, FastPolling.Interval);
        await IdleAsync(vm);

        itemPolls.Should().Be(2);
        vm.SubmitStatus!.Kind.Should().Be(AlertKind.Success);
    }

    [Fact]
    public async Task Dispose_CancelsPolling()
    {
        using var harness = new WebClientHarness(new FakeHttpMessageHandler(request => request.PathAndQuery switch
        {
            "/api/datasources/upload" => Accepted(),
            var path when path.StartsWith("/api/datasources?", StringComparison.Ordinal) => Json(HttpStatusCode.OK, "[]"),
            _ => Json(HttpStatusCode.OK, Source(UploadId, "processing"))
        }));
        var time = new ManualTimeProvider(Now);
        var polling = new PollingOptions(TimeSpan.FromMilliseconds(20), TimeSpan.FromMinutes(2));
        var vm = new DataSourcesViewModel(harness.Api, polling, time);
        var changesAfterDispose = 0;

        await vm.UploadAsync(Pdf());
        await AsleepAsync(time);
        vm.Dispose();
        vm.Changed += () => changesAfterDispose++;
        await IdleAsync(vm);
        var requestsAtStop = harness.Handler.Requests.Count;
        // Nothing is left on the clock to wake it, however far time moves on.
        time.PendingTimers.Should().Be(0);
        time.Advance(polling.Timeout);

        harness.Handler.Requests.Count.Should().Be(requestsAtStop, "no request is made once the page is gone");
        vm.SubmitStatus!.Text.Should().NotContain("still being ingested");
    }

    [Fact]
    public async Task Upload_TooLarge_IsRefusedWithoutARequest()
    {
        using var harness = new WebClientHarness(new FakeHttpMessageHandler(_ => Accepted()));
        using var vm = new DataSourcesViewModel(harness.Api, FastPolling);
        var opened = false;

        var accepted = await vm.UploadAsync(new UploadFile("big.pdf", IngestionLimits.MaxUploadBytes + 1, "application/pdf", () =>
        {
            opened = true;
            return Stream.Null;
        }));

        accepted.Should().BeFalse();
        opened.Should().BeFalse();
        harness.Handler.Requests.Should().BeEmpty();
        vm.SubmitStatus!.Kind.Should().Be(AlertKind.Error);
    }

    [Fact]
    public async Task Upload_OneByteOverFiftyMegabytes_IsRefusedNamingTheLimit()
    {
        using var harness = new WebClientHarness(new FakeHttpMessageHandler(_ => Accepted()));
        using var vm = new DataSourcesViewModel(harness.Api, FastPolling);

        var accepted = await vm.UploadAsync(new UploadFile("big.pdf", 52_428_801, "application/pdf", () => Stream.Null));

        accepted.Should().BeFalse();
        harness.Handler.Requests.Should().BeEmpty();
        vm.SubmitStatus.Should().Be(StatusMessage.Error("big.pdf is larger than the 50 MB limit."));
    }

    [Fact]
    public async Task Upload_ExactlyFiftyMegabytes_IsSent()
    {
        using var harness = new WebClientHarness(new FakeHttpMessageHandler(_ =>
            Problem(HttpStatusCode.UnsupportedMediaType, "Unsupported file type 'application/pdf'.")));
        using var vm = new DataSourcesViewModel(harness.Api, FastPolling);

        await vm.UploadAsync(new UploadFile("big.pdf", 52_428_800, "application/pdf", () => new MemoryStream([1])));

        harness.Handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task Upload_Rejected_ShowsTheProblemDetails()
    {
        using var harness = new WebClientHarness(new FakeHttpMessageHandler(_ =>
            Problem(HttpStatusCode.UnsupportedMediaType, "Unsupported file type 'image/png'.")));
        using var vm = new DataSourcesViewModel(harness.Api, FastPolling);

        (await vm.UploadAsync(new UploadFile("photo.png", 10, "image/png", () => new MemoryStream([1])))).Should().BeFalse();

        vm.SubmitStatus.Should().Be(StatusMessage.Error("Upload failed: Unsupported file type 'image/png'."));
        vm.IsSubmitting.Should().BeFalse();
        harness.Handler.Requests.Single().Body.Should().Contain("Content-Type: image/png");
    }

    [Fact]
    public async Task Delete_Error_ResetsTheFlagAndShowsTheMessage()
    {
        using var harness = new WebClientHarness(new FakeHttpMessageHandler(request => request.Method == HttpMethod.Delete
            ? Problem(HttpStatusCode.InternalServerError, "An unexpected error occurred.")
            : Json(HttpStatusCode.OK, List(Source(UploadId, "completed")))));
        using var vm = new DataSourcesViewModel(harness.Api, FastPolling);
        await vm.LoadAsync();
        Guid? deletingDuringCall = null;
        vm.Changed += () => deletingDuringCall ??= vm.DeletingId;

        await vm.DeleteAsync(UploadId);

        deletingDuringCall.Should().Be(UploadId);
        vm.DeletingId.Should().BeNull();
        vm.DeleteError.Should().Be("Delete failed: An unexpected error occurred.");
        vm.Items.Should().ContainSingle();
    }

    [Theory]
    [InlineData(HttpStatusCode.NoContent)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task Delete_SuccessOrAlreadyGone_RemovesTheRow(HttpStatusCode status)
    {
        using var harness = new WebClientHarness(new FakeHttpMessageHandler(request => request.Method == HttpMethod.Delete
            ? new HttpResponseMessage(status)
            : Json(HttpStatusCode.OK, List(Source(UploadId, "completed")))));
        using var vm = new DataSourcesViewModel(harness.Api, FastPolling);
        await vm.LoadAsync();

        await vm.DeleteAsync(UploadId);

        vm.Items.Should().BeEmpty();
        vm.DeleteError.Should().BeNull();
        vm.DeletingId.Should().BeNull();
    }

    [Fact]
    public async Task IngestWeb_Completed_IsSuccessAndReloads()
    {
        using var harness = new WebClientHarness(new FakeHttpMessageHandler(request => request.Method == HttpMethod.Post
            ? Json(HttpStatusCode.OK, $$"""{"dataSourceId":"{{UploadId}}","status":"completed","message":"Web page ingested successfully."}""")
            : Json(HttpStatusCode.OK, List(Source(UploadId, "completed")))));
        using var vm = new DataSourcesViewModel(harness.Api, FastPolling);

        (await vm.IngestWebAsync(" https://example.test/ ")).Should().BeTrue();

        harness.Handler.Requests[0].Body.Should().Be("""{"url":"https://example.test/"}""");
        vm.SubmitStatus.Should().Be(StatusMessage.Success("Web page ingested successfully."));
        vm.Items.Should().ContainSingle();
    }
}
