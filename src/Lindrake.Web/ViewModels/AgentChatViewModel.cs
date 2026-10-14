// ============================================================
// Lindrake.Web — Research Agent chat state and behaviour
// Plain C# (no Blazor types) so it is unit-testable.
// ============================================================

using System.Text;
using Lindrake.Shared.DTOs;
using Lindrake.Web.Services;

namespace Lindrake.Web.ViewModels;

/// <summary>Roles of <see cref="ConversationMessage.Role"/> (validated case-sensitively by the API).</summary>
public static class ChatRoles
{
    public const string User = "user";
    public const string Assistant = "assistant";
}

/// <summary>One bubble in the chat.</summary>
public sealed record ChatMessage(string Role, string Content)
{
    public List<string>? PluginsUsed { get; init; }
    public List<SourceCitation>? Sources { get; init; }
    public int DurationMs { get; init; }

    /// <summary>The answer was cut short (error or Stop); it is not sent back as history.</summary>
    public bool IsIncomplete { get; init; }
}

/// <summary>
/// State and actions of the Research Agent page: streams an answer token by token,
/// keeps the conversation history the API needs for follow-up questions, and builds
/// the PDF report. Raises <see cref="Changed"/> after every state change.
/// <see cref="Stop"/> cancels the running answer; <see cref="Dispose"/> cancels everything.
/// </summary>
public sealed class AgentChatViewModel(LindrakeApiClient api) : IDisposable
{
    /// <summary>The API's query length bounds.</summary>
    public const int MinQueryLength = 3;
    public const int MaxQueryLength = 5000;

    public const string ReportTitle = "Lindrake Research Report";
    public const string IncompleteResponseMessage = "The response ended before it was complete. Please try again.";

    private readonly List<ChatMessage> _messages = [];
    private readonly List<ConversationMessage> _history = [];
    private readonly StringBuilder _stream = new();
    private readonly CancellationTokenSource _disposed = new();
    private CancellationTokenSource? _turn;

    public event Action? Changed;

    public IReadOnlyList<ChatMessage> Messages => _messages;

    /// <summary>The answer received so far while <see cref="IsStreaming"/>.</summary>
    public string StreamingText { get; private set; } = string.Empty;

    public bool IsStreaming { get; private set; }
    public bool IsGeneratingReport { get; private set; }

    /// <summary>The last failure (agent error frame, HTTP error, report failure).</summary>
    public string? Error { get; private set; }

    public bool CanSend(string? query) =>
        !IsStreaming
        && query?.Trim() is { Length: >= MinQueryLength and <= MaxQueryLength };

    /// <summary>
    /// Asks the agent and streams the answer. On the <c>final</c> event the exchange
    /// joins the history; on an <c>error</c> event (or a failed request) the error is
    /// shown and any partial answer is kept, marked incomplete. Never re-runs the query.
    /// </summary>
    public async Task SendAsync(string query)
    {
        if (!CanSend(query))
            return;

        query = query.Trim();
        var request = new AgentQueryRequest
        {
            Query = query,
            ConversationHistory = _history.Count > 0 ? HistoryForRequest() : null
        };

        using var turn = CancellationTokenSource.CreateLinkedTokenSource(_disposed.Token);
        _turn = turn;
        _messages.Add(new ChatMessage(ChatRoles.User, query));
        _stream.Clear();
        StreamingText = string.Empty;
        Error = null;
        IsStreaming = true;
        Notify();

        AgentStreamFinalEvent? final = null;
        string? error = null;

        try
        {
            await foreach (var evt in api.QueryAgentStreamAsync(request, turn.Token).ConfigureAwait(false))
            {
                switch (evt)
                {
                    case SseEvent.Delta delta:
                        _stream.Append(delta.Content);
                        StreamingText = _stream.ToString();
                        Notify();
                        break;
                    case SseEvent.Final done:
                        final = done.Data;
                        break;
                    case SseEvent.Error failed:
                        error = failed.Message;
                        break;
                }

                if (final is not null || error is not null)
                    break;
            }
        }
        catch (OperationCanceledException) when (turn.IsCancellationRequested)
        {
            // Stop, or the page closing: handled below.
        }
        finally
        {
            _turn = null;
            IsStreaming = false;
        }

        if (_disposed.IsCancellationRequested)
            return; // page closed

        var stopped = final is null && error is null && turn.IsCancellationRequested;
        if (final is null && error is null && !stopped)
            error = IncompleteResponseMessage;

        var text = _stream.ToString();
        _stream.Clear();
        StreamingText = string.Empty;

        if (final is not null)
        {
            _messages.Add(new ChatMessage(ChatRoles.Assistant, text)
            {
                PluginsUsed = final.PluginsUsed,
                Sources = final.Sources,
                DurationMs = final.DurationMs
            });
            _history.Add(new ConversationMessage { Role = ChatRoles.User, Content = Truncate(query) });
            _history.Add(new ConversationMessage { Role = ChatRoles.Assistant, Content = Truncate(text) });
        }
        else
        {
            if (text.Length > 0)
                _messages.Add(new ChatMessage(ChatRoles.Assistant, text) { IsIncomplete = true });

            Error = stopped ? null : error;
        }

        Notify();
    }

    /// <summary>Cancels the answer being streamed (the partial text is kept).</summary>
    public void Stop() => _turn?.Cancel();

    /// <summary>Builds the PDF report of the conversation. Null (and <see cref="Error"/> set) on failure.</summary>
    public async Task<byte[]?> GenerateReportAsync()
    {
        if (IsGeneratingReport || IsStreaming || _messages.Count == 0)
            return null;

        IsGeneratingReport = true;
        Error = null;
        Notify();

        try
        {
            var request = new GenerateReportRequest
            {
                Title = ReportTitle,
                Messages = [.. _messages.Select(m => new ReportTurn
                {
                    Role = m.Role,
                    Content = m.Content,
                    Citations = m.Sources ?? []
                })]
            };

            var result = await api.GenerateReportPdfAsync(request, _disposed.Token).ConfigureAwait(false);
            if (result.IsSuccess)
                return result.Value;

            Error = $"PDF export failed: {result.Error.Message}";
            return null;
        }
        catch (OperationCanceledException) when (_disposed.IsCancellationRequested)
        {
            return null;
        }
        finally
        {
            IsGeneratingReport = false;
            Notify();
        }
    }

    /// <summary>Shows <paramref name="message"/> as the page error (e.g. a failed browser download).</summary>
    public void ReportError(string message)
    {
        Error = message;
        Notify();
    }

    /// <summary>Cancels the running answer and report; no <see cref="Changed"/> is raised afterwards.</summary>
    public void Dispose()
    {
        Changed = null;
        _disposed.Cancel();
    }

    // The API keeps the most recent Agent:MaxHistoryMessages turns itself; the client
    // only stays within the request's validation limit so a long chat keeps working.
    private List<ConversationMessage> HistoryForRequest() =>
        _history.Count > AgentQueryRequest.MaxConversationTurns
            ? _history.GetRange(_history.Count - AgentQueryRequest.MaxConversationTurns, AgentQueryRequest.MaxConversationTurns)
            : [.. _history];

    private static string Truncate(string content) =>
        content.Length > ConversationMessage.MaxContentLength ? content[..ConversationMessage.MaxContentLength] : content;

    private void Notify() => Changed?.Invoke();
}
