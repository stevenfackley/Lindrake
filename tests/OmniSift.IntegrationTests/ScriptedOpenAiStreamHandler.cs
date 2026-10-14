// ============================================================
// Integration Tests — scripted OpenAI streaming endpoint
// ============================================================

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.SemanticKernel;

namespace OmniSift.IntegrationTests;

/// <summary>
/// HTTP handler impersonating the OpenAI chat-completions endpoint in streaming
/// mode, so a test can drive SK's real OpenAI connector — the production one,
/// with its auto-function-calling loop and filters — without a network.
/// Each request is answered with the next scripted SSE body.
/// </summary>
internal sealed class ScriptedOpenAiStreamHandler(params string[] responses) : HttpMessageHandler
{
    private readonly Queue<string> _responses = new(responses);

    /// <summary>A streamed response asking the client to call <paramref name="functionName"/>.</summary>
    public static string ToolCall(string functionName, object arguments) => Stream(
        Chunk(new
        {
            role = "assistant",
            tool_calls = new[]
            {
                new
                {
                    index = 0,
                    id = "call_1",
                    type = "function",
                    function = new { name = functionName, arguments = JsonSerializer.Serialize(arguments) }
                }
            }
        }, finishReason: null),
        Chunk(new { }, finishReason: "tool_calls"));

    /// <summary>A streamed text answer, one chunk per argument.</summary>
    public static string Text(params string[] chunks) => Stream(
        [.. chunks.Select(c => Chunk(new { role = "assistant", content = c }, finishReason: null)),
         Chunk(new { }, finishReason: "stop")]);

    /// <summary>Adds SK's OpenAI connector, talking to <paramref name="handler"/>, to a kernel.</summary>
    public static void AddTo(IKernelBuilder builder, ScriptedOpenAiStreamHandler handler) =>
        builder.AddOpenAIChatCompletion(
            modelId: "test-model",
            apiKey: "test-key",
            httpClient: new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri("https://llm.test/v1/") });

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (_responses.Count == 0)
            throw new InvalidOperationException("No scripted LLM response left.");

        // A network body is not seekable; the OpenAI SDK's transport refuses to
        // dispose a partially read seekable one (e.g. StringContent's MemoryStream).
        var content = new StreamContent(new ForwardOnlyStream(Encoding.UTF8.GetBytes(_responses.Dequeue())));
        content.Headers.ContentType = new MediaTypeHeaderValue("text/event-stream");

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
    }

    private sealed class ForwardOnlyStream(byte[] bytes) : Stream
    {
        private readonly MemoryStream _inner = new(bytes);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _inner.Dispose();
            base.Dispose(disposing);
        }
    }

    private static string Stream(params string[] chunks) =>
        string.Concat(chunks.Select(c => $"data: {c}\n\n")) + "data: [DONE]\n\n";

    private static string Chunk(object delta, string? finishReason) => JsonSerializer.Serialize(new
    {
        id = "chatcmpl-test",
        @object = "chat.completion.chunk",
        created = 1_700_000_000,
        model = "test-model",
        choices = new[] { new { index = 0, delta, finish_reason = finishReason } }
    });
}
