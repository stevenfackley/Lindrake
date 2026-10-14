// ============================================================
// OmniSift.Api — Semantic Kernel helpers
// ============================================================

using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using OmniSift.Api.Infrastructure;

namespace OmniSift.Api.Services;

public static class KernelExtensions
{
    /// <summary>
    /// The kernel's chat-completion service, or <see cref="LlmNotConfiguredException"/>
    /// (503) when none is registered (Anthropic:ApiKey empty).
    /// </summary>
    public static IChatCompletionService GetChatCompletionOrThrow(this Kernel kernel) =>
        kernel.Services.GetService<IChatCompletionService>() ?? throw new LlmNotConfiguredException();
}
