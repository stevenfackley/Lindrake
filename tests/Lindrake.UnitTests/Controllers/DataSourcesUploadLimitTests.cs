// ============================================================
// Unit Tests — DataSourcesController upload size limit
// Pins the 50 MB limit on both of its server-side checks: the
// request body limit and the file-length check in the action.
// ============================================================

using System.Reflection;
using FluentAssertions;
using Lindrake.Api.Controllers;
using Lindrake.Api.Middleware;
using Lindrake.Api.Services;
using Lindrake.Api.Services.Extraction;
using Lindrake.Api.Services.Ingestion;
using Lindrake.UnitTests.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Lindrake.UnitTests.Controllers;

public sealed class DataSourcesUploadLimitTests
{
    [Fact]
    public void Upload_RequestBodyLimit_IsFiftyMegabytes()
    {
        var attribute = typeof(DataSourcesController)
            .GetMethod(nameof(DataSourcesController.Upload))!
            .GetCustomAttribute<RequestSizeLimitAttribute>();

        attribute.Should().NotBeNull();
        ((IRequestSizeLimitMetadata)attribute!).MaxRequestBodySize.Should().Be(52_428_800);
    }

    [Fact]
    public async Task Upload_OneByteOverFiftyMegabytes_Returns400NamingTheLimit()
    {
        var problem = await UploadAsync(length: 52_428_801, fileName: "big.pdf");

        problem.Status.Should().Be(StatusCodes.Status400BadRequest);
        problem.Detail.Should().Be("File size exceeds the maximum allowed size of 50 MB.");
    }

    [Fact]
    public async Task Upload_ExactlyFiftyMegabytes_PassesTheSizeCheck()
    {
        // A 513-character name stops the upload at the next check, so no 50 MB buffer is needed.
        var problem = await UploadAsync(length: 52_428_800, fileName: new string('a', 509) + ".pdf");

        problem.Status.Should().Be(StatusCodes.Status400BadRequest);
        problem.Detail.Should().Be("File name exceeds the maximum length of 512 characters.");
    }

    private static async Task<ProblemDetails> UploadAsync(long length, string fileName)
    {
        var file = new Mock<IFormFile>();
        file.SetupGet(f => f.Length).Returns(length);
        file.SetupGet(f => f.FileName).Returns(fileName);

        using var db = InMemoryDb.Create("upload-limit-" + Guid.NewGuid());
        var controller = new DataSourcesController(
            db,
            new TenantContext(),
            Mock.Of<IIngestionPipeline>(),
            Mock.Of<ITextExtractorRegistry>(),
            new IngestionChannel(),
            Mock.Of<IAuditLogger>(),
            NullLogger<DataSourcesController>.Instance);

        var result = await controller.Upload(file.Object, sourceType: null, CancellationToken.None);

        return result.Result.Should().BeOfType<ObjectResult>()
            .Which.Value.Should().BeOfType<ProblemDetails>().Subject;
    }
}
