// ============================================================
// OmniSift.Api — Embedding options startup validation
// ============================================================

using Microsoft.Extensions.Options;

namespace OmniSift.Api.Options;

/// <summary>
/// Fail-fast guard for the OpenAI embedding provider. OpenAI emits
/// <see cref="OpenAiOptions.EmbeddingDimensions"/>-wide vectors (3072 by default)
/// while the pgvector column is <c>vector(Embedding:Dimensions)</c> (384 for
/// bge-small-en-v1.5). Switching providers without a column migration would
/// insert wrong-width vectors and fail at runtime; this surfaces it at startup.
/// </summary>
internal sealed class EmbeddingOptionsValidator(IOptions<OpenAiOptions> openAiOptions)
    : IValidateOptions<EmbeddingOptions>
{
    public ValidateOptionsResult Validate(string? name, EmbeddingOptions options)
    {
        if (options.Provider != EmbeddingProvider.OpenAi)
            return ValidateOptionsResult.Success;

        var openAi = openAiOptions.Value;
        var failures = new List<string>();

        if (options.Dimensions != openAi.EmbeddingDimensions)
        {
            failures.Add(
                $"Provider=OpenAi but Embedding:Dimensions ({options.Dimensions}) != " +
                $"OpenAI:EmbeddingDimensions ({openAi.EmbeddingDimensions}). " +
                $"The pgvector column is vector({options.Dimensions}). " +
                "You must create an EF migration to change the column before switching providers.");
        }

        if (string.IsNullOrWhiteSpace(openAi.ApiKey))
            failures.Add("Provider=OpenAi requires OpenAI:ApiKey.");

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
