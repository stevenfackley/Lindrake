// ============================================================
// Lindrake.Api — Semantic Kernel helpers
// ============================================================

using Lindrake.Api.Infrastructure;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Lindrake.Api.Services;

public static class KernelExtensions
{
    /// <summary>
    /// The kernel's chat-completion service, or <see cref="LlmNotConfiguredException"/>
    /// (503) when none is registered (Anthropic:ApiKey empty).
    /// </summary>
    public static IChatCompletionService GetChatCompletionOrThrow(this Kernel kernel) =>
        kernel.Services.GetService<IChatCompletionService>() ?? throw new LlmNotConfiguredException();
}
