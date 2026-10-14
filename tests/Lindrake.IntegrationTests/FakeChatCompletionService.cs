// ============================================================
// Integration Tests — fake chat-completion backend
// ============================================================

using System.Runtime.CompilerServices;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace OmniSift.IntegrationTests;

/// <summary>
/// <see cref="IChatCompletionService"/> that answers with fixed text chunks
/// (no function calling), optionally failing after the last one.
/// </summary>
internal sealed class FakeChatCompletionService(params string[] chunks) : IChatCompletionService
{
    /// <summary>Thrown after the chunks have been streamed.</summary>
    public Exception? FailAfterChunks { get; init; }

    public IReadOnlyDictionary<string, object?> Attributes { get; } = new Dictionary<string, object?>();

    public Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
        ChatHistory chatHistory,
        PromptExecutionSettings? executionSettings = null,
        Kernel? kernel = null,
        CancellationToken cancellationToken = default)
    {
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
        foreach (var chunk in chunks)
        {
            await Task.Yield();
            yield return new StreamingChatMessageContent(AuthorRole.Assistant, chunk);
        }

        if (FailAfterChunks is not null)
            throw FailAfterChunks;
    }
}
