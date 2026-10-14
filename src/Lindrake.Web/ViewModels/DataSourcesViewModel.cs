// ============================================================
// Lindrake.Web — Data Sources page state and behaviour
// Plain C# (no Blazor types) so it is unit-testable.
// ============================================================

using Lindrake.Shared.DTOs;
using Lindrake.Web.Services;

namespace Lindrake.Web.ViewModels;

/// <summary>A file picked for upload; <see cref="OpenRead"/> is called only if the size is allowed.</summary>
public sealed record UploadFile(string Name, long Size, string? ContentType, Func<Stream> OpenRead);

/// <summary>How often, and for how long, an upload's status is polled after the 202.</summary>
public sealed record PollingOptions(TimeSpan Interval, TimeSpan Timeout)
{
    public static PollingOptions Default { get; } = new(TimeSpan.FromSeconds(2), TimeSpan.FromMinutes(2));
}

/// <summary>
/// State and actions of the Data Sources page: the paged list, uploads (with
/// status polling until the background ingestion finishes), web ingestion and
/// deletion. Raises <see cref="Changed"/> after every state change; the page
/// re-renders on it. <see cref="Dispose"/> cancels in-flight calls and polls.
/// </summary>
public sealed class DataSourcesViewModel(LindrakeApiClient api, PollingOptions? polling = null) : IDisposable
{
    /// <summary>The API's upload limit (50 MB).</summary>
    public const long MaxUploadBytes = 50L * 1024 * 1024;

    /// <summary>Rows per request (the API defaults to 200, caps at 500).</summary>
    public const int PageSize = 200;

    private readonly PollingOptions _polling = polling ?? PollingOptions.Default;
    private readonly CancellationTokenSource _disposed = new();
    private readonly List<Task> _polls = [];
    private readonly List<DataSourceDto> _items = [];

    public event Action? Changed;

    public IReadOnlyList<DataSourceDto> Items => _items;
    public bool IsLoading { get; private set; }
    public string? LoadError { get; private set; }

    /// <summary>The last page came back full: there may be more rows.</summary>
    public bool HasMore { get; private set; }

    /// <summary>An upload or web ingestion request is in flight.</summary>
    public bool IsSubmitting { get; private set; }

    /// <summary>Outcome of the latest upload / web ingestion (including its polling).</summary>
    public StatusMessage? SubmitStatus { get; private set; }

    /// <summary>The row being deleted, if any.</summary>
    public Guid? DeletingId { get; private set; }
    public string? DeleteError { get; private set; }

    /// <summary>Loads the first page, replacing the list.</summary>
    public Task LoadAsync() => LoadPageAsync(skip: 0);

    /// <summary>Appends the next page.</summary>
    public Task LoadMoreAsync() => HasMore ? LoadPageAsync(skip: _items.Count) : Task.CompletedTask;

    /// <summary>
    /// Uploads <paramref name="file"/> with its real MIME type, then polls its status
    /// in the background until it is <c>completed</c>/<c>failed</c>. Returns true when
    /// the API accepted the file.
    /// </summary>
    public async Task<bool> UploadAsync(UploadFile file, string? sourceType = null)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (IsSubmitting)
            return false;

        if (file.Size > MaxUploadBytes)
        {
            SubmitStatus = StatusMessage.Error($"{file.Name} is larger than the {MaxUploadBytes / (1024 * 1024)} MB limit.");
            Notify();
            return false;
        }

        IsSubmitting = true;
        SubmitStatus = null;
        Notify();

        try
        {
            var contentType = UploadContentTypes.ForFileName(file.Name, file.ContentType);
            var result = await api
                .UploadFileAsync(file.OpenRead(), file.Name, contentType, sourceType, _disposed.Token)
                .ConfigureAwait(false);

            if (!result.IsSuccess)
            {
                SubmitStatus = StatusMessage.Error($"Upload failed: {result.Error.Message}");
                return false;
            }

            SubmitStatus = StatusMessage.Info($"{file.Name} uploaded — ingesting…");
            StartPolling(result.Value.DataSourceId, file.Name);
            return true;
        }
        catch (IOException ex)
        {
            // Reading the picked file failed (e.g. it changed or is unreadable).
            SubmitStatus = StatusMessage.Error($"Upload failed: {ex.Message}");
            return false;
        }
        catch (OperationCanceledException) when (_disposed.IsCancellationRequested)
        {
            return false;
        }
        finally
        {
            IsSubmitting = false;
            Notify();
        }
    }

    /// <summary>Fetches and ingests a web page synchronously. Returns true when it was ingested.</summary>
    public async Task<bool> IngestWebAsync(string url)
    {
        if (IsSubmitting || string.IsNullOrWhiteSpace(url))
            return false;

        IsSubmitting = true;
        SubmitStatus = null;
        Notify();

        try
        {
            var result = await api.IngestWebAsync(url.Trim(), _disposed.Token).ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                SubmitStatus = StatusMessage.Error($"Web ingestion failed: {result.Error.Message}");
                return false;
            }

            var completed = result.Value.Status == IngestionStatuses.Completed;
            SubmitStatus = completed
                ? StatusMessage.Success(result.Value.Message)
                : StatusMessage.Error(result.Value.Message);

            await LoadPageAsync(skip: 0).ConfigureAwait(false);
            return completed;
        }
        catch (OperationCanceledException) when (_disposed.IsCancellationRequested)
        {
            return false;
        }
        finally
        {
            IsSubmitting = false;
            Notify();
        }
    }

    /// <summary>Deletes a data source. One delete at a time; the flag is always reset.</summary>
    public async Task DeleteAsync(Guid id)
    {
        if (DeletingId is not null)
            return;

        DeletingId = id;
        DeleteError = null;
        Notify();

        try
        {
            var result = await api.DeleteDataSourceAsync(id, _disposed.Token).ConfigureAwait(false);

            // 404: someone else already deleted it — the row is gone either way.
            if (result.IsSuccess || result.Error.IsNotFound)
                _items.RemoveAll(s => s.Id == id);
            else
                DeleteError = $"Delete failed: {result.Error.Message}";
        }
        catch (OperationCanceledException) when (_disposed.IsCancellationRequested)
        {
            // Page closed mid-delete.
        }
        finally
        {
            DeletingId = null;
            Notify();
        }
    }

    /// <summary>Completes when every status poll started so far has finished (test hook).</summary>
    public Task WhenPollingIdleAsync() => Task.WhenAll(_polls);

    /// <summary>Cancels in-flight requests and polls; no <see cref="Changed"/> is raised afterwards.</summary>
    public void Dispose()
    {
        Changed = null;
        _disposed.Cancel();
    }

    private async Task LoadPageAsync(int skip)
    {
        IsLoading = true;
        LoadError = null;
        Notify();

        try
        {
            var result = await api.GetDataSourcesAsync(skip, PageSize, _disposed.Token).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                if (skip == 0)
                    _items.Clear();
                _items.AddRange(result.Value);
                HasMore = result.Value.Count == PageSize;
            }
            else
            {
                LoadError = $"Failed to load data sources: {result.Error.Message}";
            }
        }
        catch (OperationCanceledException) when (_disposed.IsCancellationRequested)
        {
            // Page closed mid-load.
        }
        finally
        {
            IsLoading = false;
            Notify();
        }
    }

    private void StartPolling(Guid id, string name)
    {
        _polls.RemoveAll(p => p.IsCompleted);
        _polls.Add(PollUntilTerminalAsync(id, name));
    }

    private async Task PollUntilTerminalAsync(Guid id, string name)
    {
        // Show the new pending row straight away, then follow its status.
        await LoadPageAsync(skip: 0).ConfigureAwait(false);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_disposed.Token);
        deadline.CancelAfter(_polling.Timeout);

        try
        {
            while (true)
            {
                await Task.Delay(_polling.Interval, deadline.Token).ConfigureAwait(false);

                var result = await api.GetDataSourceAsync(id, deadline.Token).ConfigureAwait(false);
                if (!result.IsSuccess)
                {
                    if (result.Error.IsTransient)
                        continue;

                    SubmitStatus = StatusMessage.Error($"Could not check the status of {name}: {result.Error.Message}");
                    Notify();
                    return;
                }

                var source = result.Value;
                var index = _items.FindIndex(s => s.Id == id);
                if (index >= 0)
                    _items[index] = source;

                if (source.Status == IngestionStatuses.Completed)
                {
                    SubmitStatus = StatusMessage.Success($"{name} ingested ({source.ChunkCount} chunks).");
                    Notify();
                    return;
                }

                if (source.Status == IngestionStatuses.Failed)
                {
                    SubmitStatus = StatusMessage.Error($"{name} failed: {source.ErrorMessage ?? "unknown error"}");
                    Notify();
                    return;
                }

                Notify();
            }
        }
        catch (OperationCanceledException) when (!_disposed.IsCancellationRequested)
        {
            SubmitStatus = StatusMessage.Info(
                $"{name} is still being ingested. Refresh the list later to see the result.");
            Notify();
        }
        catch (OperationCanceledException)
        {
            // Page closed: stop quietly.
        }
    }

    private void Notify() => Changed?.Invoke();
}
