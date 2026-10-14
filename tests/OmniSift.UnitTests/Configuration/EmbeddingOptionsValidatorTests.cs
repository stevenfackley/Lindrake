// ============================================================
// Unit Tests — EmbeddingOptionsValidator
// Startup guard against OpenAI/pgvector dimension mismatches
// ============================================================

using FluentAssertions;
using OmniSift.Api.Options;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace OmniSift.UnitTests.Configuration;

public sealed class EmbeddingOptionsValidatorTests
{
    private static EmbeddingOptionsValidator CreateValidator(int openAiDimensions = 384, string apiKey = "sk-test") =>
        new(MsOptions.Create(new OpenAiOptions { EmbeddingDimensions = openAiDimensions, ApiKey = apiKey }));

    [Fact]
    public void Onnx_IgnoresOpenAiSettings()
    {
        var result = CreateValidator(openAiDimensions: 3072, apiKey: "")
            .Validate(null, new EmbeddingOptions { Provider = EmbeddingProvider.Onnx, Dimensions = 384 });

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void OpenAi_DimensionMismatch_Fails()
    {
        var result = CreateValidator(openAiDimensions: 3072)
            .Validate(null, new EmbeddingOptions { Provider = EmbeddingProvider.OpenAi, Dimensions = 384 });

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Embedding:Dimensions (384) != OpenAI:EmbeddingDimensions (3072)");
    }

    [Fact]
    public void OpenAi_MissingApiKey_Fails()
    {
        var result = CreateValidator(apiKey: " ")
            .Validate(null, new EmbeddingOptions { Provider = EmbeddingProvider.OpenAi, Dimensions = 384 });

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("OpenAI:ApiKey");
    }

    [Fact]
    public void OpenAi_MatchingDimensionsAndKey_Succeeds()
    {
        var result = CreateValidator()
            .Validate(null, new EmbeddingOptions { Provider = EmbeddingProvider.OpenAi, Dimensions = 384 });

        result.Succeeded.Should().BeTrue();
    }
}
