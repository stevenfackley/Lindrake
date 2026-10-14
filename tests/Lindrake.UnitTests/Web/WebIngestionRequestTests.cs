// ============================================================
// Unit Tests — WebIngestionRequest validation
// ============================================================

using System.ComponentModel.DataAnnotations;
using FluentAssertions;
using Lindrake.Shared.DTOs;

namespace Lindrake.UnitTests.Web;

public sealed class WebIngestionRequestTests
{
    [Fact]
    public void Url_LongerThanTheOriginalUrlColumn_IsInvalid()
    {
        // data_sources.original_url is varchar(2048); longer would fail the insert (500).
        var request = new WebIngestionRequest { Url = "https://example.com/" + new string('a', 2048) };

        Validate(request).Should().ContainSingle(r => r.MemberNames.Contains(nameof(WebIngestionRequest.Url)));
    }

    [Fact]
    public void Url_WithinLimit_IsValid()
    {
        Validate(new WebIngestionRequest { Url = "https://example.com/page" }).Should().BeEmpty();
    }

    private static List<ValidationResult> Validate(WebIngestionRequest request)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true);
        return results;
    }
}
