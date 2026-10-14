// ============================================================
// Integration Tests — RFC 7807 response assertions
// Every API error, including the framework's own 401/403/429,
// must reach the client as application/problem+json.
// ============================================================

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;

namespace Lindrake.IntegrationTests;

internal static class ProblemAssertions
{
    /// <summary>
    /// Asserts <paramref name="response"/> is a ProblemDetails body for
    /// <paramref name="status"/> with a title and a traceId, and returns it.
    /// </summary>
    public static async Task<ProblemDetails> ShouldBeProblemAsync(HttpResponseMessage response, HttpStatusCode status)
    {
        response.StatusCode.Should().Be(status);
        response.Content.Headers.ContentType.Should().NotBeNull();
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem.Should().NotBeNull();
        problem!.Status.Should().Be((int)status);
        problem.Title.Should().NotBeNullOrWhiteSpace();
        problem.Extensions.Should().ContainKey("traceId");
        return problem;
    }
}
