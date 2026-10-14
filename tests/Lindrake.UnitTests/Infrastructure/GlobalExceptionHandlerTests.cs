// ============================================================
// Unit Tests — GlobalExceptionHandler
// Domain exceptions map to their status; everything else is a
// generic 500 that never echoes the exception message.
// ============================================================

using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using OmniSift.Api.Infrastructure;

namespace OmniSift.UnitTests.Infrastructure;

public sealed class GlobalExceptionHandlerTests
{
    private readonly Mock<ILogger<GlobalExceptionHandler>> _logger = new();

    [Fact]
    public async Task UnsupportedSourceType_Returns400WithMessage()
    {
        var (status, body) = await HandleAsync(new UnsupportedSourceTypeException("xls", ["pdf", "sms"]));

        status.Should().Be(StatusCodes.Status400BadRequest);
        body.GetProperty("detail").GetString().Should().Be("Unsupported source type: 'xls'. Allowed: pdf, sms");
        VerifyLogged(LogLevel.Warning);
    }

    [Fact]
    public async Task TenantContextMissing_Returns403()
    {
        var (status, _) = await HandleAsync(new TenantContextMissingException());

        status.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task LlmNotConfigured_Returns503WithMessage()
    {
        var (status, body) = await HandleAsync(new LlmNotConfiguredException());

        status.Should().Be(StatusCodes.Status503ServiceUnavailable);
        body.GetProperty("detail").GetString().Should().Contain("LLM service not configured");
    }

    [Fact]
    public async Task UnknownException_Returns500WithGenericDetailAndTraceId()
    {
        var ex = new InvalidOperationException("Host=db;Password=hunter2 exploded");

        var (status, body) = await HandleAsync(ex);

        status.Should().Be(StatusCodes.Status500InternalServerError);
        body.GetProperty("detail").GetString().Should().Be("An unexpected error occurred.");
        body.GetRawText().Should().NotContain("hunter2");
        body.GetProperty("traceId").GetString().Should().NotBeNullOrEmpty();
        VerifyLogged(LogLevel.Error, ex);
    }

    [Fact]
    public async Task ArgumentException_IsNotAClientError()
    {
        var (status, _) = await HandleAsync(new ArgumentException("internal precondition"));

        status.Should().Be(StatusCodes.Status500InternalServerError);
    }

    [Fact]
    public async Task HttpRequestException_Returns502WithGenericDetail()
    {
        var (status, body) = await HandleAsync(new HttpRequestException("10.0.0.5:443 refused"));

        status.Should().Be(StatusCodes.Status502BadGateway);
        body.GetRawText().Should().NotContain("10.0.0.5");
    }

    [Fact]
    public async Task OperationCanceled_IsNotHandled()
    {
        var handler = new GlobalExceptionHandler(Mock.Of<Microsoft.AspNetCore.Http.IProblemDetailsService>(), _logger.Object);

        var handled = await handler.TryHandleAsync(new DefaultHttpContext(), new OperationCanceledException(), CancellationToken.None);

        handled.Should().BeFalse();
    }

    private async Task<(int Status, JsonElement Body)> HandleAsync(Exception exception)
    {
        var services = new ServiceCollection().AddLogging().AddProblemDetails().BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Method = "POST";
        context.Request.Path = "/api/test";
        context.Response.Body = new MemoryStream();

        var handler = new GlobalExceptionHandler(services.GetRequiredService<IProblemDetailsService>(), _logger.Object);
        var handled = await handler.TryHandleAsync(context, exception, CancellationToken.None);

        handled.Should().BeTrue();
        context.Response.Body.Position = 0;
        using var json = await JsonDocument.ParseAsync(context.Response.Body);
        return (context.Response.StatusCode, json.RootElement.Clone());
    }

    private void VerifyLogged(LogLevel level, Exception? exception = null) =>
        _logger.Verify(
            l => l.Log(
                level,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                exception ?? It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
}
