// ============================================================
// Unit Tests — IngestionStatus wire strings
// These strings are an API contract and the data_sources.status
// column's contents: they must not change.
// ============================================================

using FluentAssertions;
using Lindrake.Api.Models;
using Lindrake.Shared.DTOs;

namespace Lindrake.UnitTests.Models;

public sealed class IngestionStatusExtensionsTests
{
    [Theory]
    [InlineData(IngestionStatus.Pending, "pending")]
    [InlineData(IngestionStatus.Processing, "processing")]
    [InlineData(IngestionStatus.Completed, "completed")]
    [InlineData(IngestionStatus.Failed, "failed")]
    public void ToWireString_IsTheLowercaseName_AndRoundTrips(IngestionStatus status, string wire)
    {
        status.ToWireString().Should().Be(wire);
        IngestionStatusExtensions.ParseWireString(wire).Should().Be(status);
    }

    [Fact]
    public void EveryStatus_HasAWireString()
    {
        foreach (var status in Enum.GetValues<IngestionStatus>())
            status.ToWireString().Should().Be(status.ToString().ToLowerInvariant());
    }

    [Theory]
    [InlineData(IngestionStatus.Pending, IngestionStatuses.Pending, false)]
    [InlineData(IngestionStatus.Processing, IngestionStatuses.Processing, false)]
    [InlineData(IngestionStatus.Completed, IngestionStatuses.Completed, true)]
    [InlineData(IngestionStatus.Failed, IngestionStatuses.Failed, true)]
    public void SharedConstants_MatchTheWireStrings(IngestionStatus status, string constant, bool terminal)
    {
        status.ToWireString().Should().Be(constant);
        IngestionStatuses.IsTerminal(constant).Should().Be(terminal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("done")]
    public void IsTerminal_UnknownStatus_IsFalse(string? status)
    {
        IngestionStatuses.IsTerminal(status).Should().BeFalse();
    }

    [Fact]
    public void Parse_IsCaseInsensitive()
    {
        IngestionStatusExtensions.ParseWireString("Completed").Should().Be(IngestionStatus.Completed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("done")]
    public void TryParse_RejectsUnknownStrings(string? value)
    {
        IngestionStatusExtensions.TryParseWireString(value, out _).Should().BeFalse();
        var parse = () => IngestionStatusExtensions.ParseWireString(value!);
        parse.Should().Throw<FormatException>();
    }
}
