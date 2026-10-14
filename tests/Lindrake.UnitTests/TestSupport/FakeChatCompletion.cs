// ============================================================
// Unit Tests — fake LLM backends for Semantic Kernel
// ============================================================

using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Lindrake.UnitTests.TestSupport;

/// <summary>
/// <see cref="IChatCompletionService"/> that answers with fixed text chunks and
/// records the chat history it was given. Does no function calling.
/// </summary>
internal sealed class ScriptedChatCompletionService(params string[] chunks) : IChatCompletionService
{
    /// <summary>Runs after the last streamed chunk, before the stream completes.</summary>
    public Action? OnStreamCompleted { get; init; }

    /// <summary>Thrown after the streamed chunks instead of completing.</summary>
    public Exception? FailAfterChunks { get; init; }

    public ChatHistory? LastHistory { get; private set; }

    public IReadOnlyDictionary<string, object?> Attributes { get; } = new Dictionary<string, object?>();

    public Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
        ChatHistory chatHistory,
        PromptExecutionSettings? executionSettings = null,
        Kernel? kernel = null,
        CancellationToken cancellationToken = default)
    {
        LastHistory = chatHistory;
        IReadOnlyList<ChatMessageContent> result =
            [new ChatMessageContent(AuthorRole.Assistant, string.Concat(chunks))];
        return Task.FromResult(result);
    }

    public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
        ChatHistory chatHistory,
        PromptExecutionSettings? executionSettings = null,
        Kernel? kernel = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        LastHistory = chatHistory;

        foreach (var chunk in chunks)
        {
            await Task.Yield();
            yield return new StreamingChatMessageContent(AuthorRole.Assistant, chunk);
        }

        OnStreamCompleted?.Invoke();

        if (FailAfterChunks is not null)
            throw FailAfterChunks;
    }
}

/// <summary>
/// HTTP handler impersonating the OpenAI chat-completions endpoint, so tests can
/// drive SK's real OpenAI connector (the production one) — including its
/// auto-function-calling loop and filters — without a network.
/// Each request is answered with the next scripted (non-streaming) response.
/// </summary>
internal sealed class ScriptedOpenAiHandler(params string[] responses) : HttpMessageHandler
{
    private readonly Queue<string> _responses = new(responses);

    public List<string> RequestBodies { get; } = [];

    /// <summary>A response asking the client to call <paramref name="functionName"/>.</summary>
    public static string ToolCall(string functionName, object arguments) => Completion(
        new
        {
            role = "assistant",
            content = (string?)null,
            tool_calls = new[]
            {
                new
                {
                    id = "call_1",
                    type = "function",
                    function = new { name = functionName, arguments = JsonSerializer.Serialize(arguments) }
                }
            }
        },
        "tool_calls");

    /// <summary>A plain text answer.</summary>
    public static string Text(string content) => Completion(new { role = "assistant", content }, "stop");

    /// <summary>A kernel that uses SK's OpenAI connector over <paramref name="handler"/>.</summary>
    public static IKernelBuilder KernelBuilder(ScriptedOpenAiHandler handler)
    {
        var builder = Kernel.CreateBuilder();
        builder.AddOpenAIChatCompletion(
            modelId: "test-model",
            apiKey: "test-key",
            httpClient: new HttpClient(handler) { BaseAddress = new Uri("https://llm.test/v1/") });
        return builder;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        RequestBodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));

        if (_responses.Count == 0)
            throw new InvalidOperationException("No scripted LLM response left.");

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(_responses.Dequeue(), Encoding.UTF8, "application/json")
        };
    }

    private static string Completion(object message, string finishReason) => JsonSerializer.Serialize(new
    {
        id = "chatcmpl-test",
        @object = "chat.completion",
        created = 1_700_000_000,
        model = "test-model",
        choices = new[] { new { index = 0, message, finish_reason = finishReason } },
        usage = new { prompt_tokens = 1, completion_tokens = 1, total_tokens = 2 }
    });
}
