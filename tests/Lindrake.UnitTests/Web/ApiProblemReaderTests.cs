using FluentAssertions;
using Lindrake.Web.Services;

namespace Lindrake.UnitTests.Web;

public sealed class ApiProblemReaderTests
{
    [Fact]
    public void Detail_IsPreferredOverTitle()
    {
        var error = ApiProblemReader.FromBody(409, """{"title":"Conflict","status":409,"detail":"An account with this email address already exists."}""");

        error.Should().Be(new ApiError(409, "An account with this email address already exists."));
    }

    [Fact]
    public void Title_IsUsedWithoutDetail()
    {
        ApiProblemReader.FromBody(502, """{"title":"Upstream Service Error","status":502}""")
            .Message.Should().Be("Upstream Service Error");
    }

    [Fact]
    public void ValidationErrors_AreListed()
    {
        const string body = """
            {"title":"One or more validation errors occurred.","status":400,
             "errors":{"Query":["Query must be at least 3 characters."],"ConversationHistory[0].Role":["Role must be 'user' or 'assistant'."]}}
            """;

        ApiProblemReader.FromBody(400, body).Message.Should().Be(
            "Query must be at least 3 characters. Role must be 'user' or 'assistant'.");
    }

    [Theory]
    [InlineData(401, "You are not signed in, or your session has expired.")]
    [InlineData(404, "The requested item was not found.")]
    [InlineData(429, "Too many requests. Wait a moment and try again.")]
    [InlineData(503, "The server could not complete the request (HTTP 503).")]
    [InlineData(418, "The request failed (HTTP 418).")]
    public void EmptyBody_FallsBackToTheStatus(int status, string message)
    {
        ApiProblemReader.FromBody(status, string.Empty).Should().Be(new ApiError(status, message));
    }

    [Theory]
    [InlineData("<html><body>502 Bad Gateway</body></html>")]
    [InlineData("[1,2,3]")]
    [InlineData("""{"detail":42}""")]
    public void BodyThatIsNotAProblem_FallsBackToTheStatus(string body)
    {
        ApiProblemReader.FromBody(502, body).Message.Should().Be("The server could not complete the request (HTTP 502).");
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(429, true)]
    [InlineData(500, true)]
    [InlineData(503, true)]
    [InlineData(400, false)]
    [InlineData(401, false)]
    [InlineData(404, false)]
    public void IsTransient_CoversNetworkServerAndRateLimit(int status, bool transient)
    {
        new ApiError(status, "x").IsTransient.Should().Be(transient);
    }
}
