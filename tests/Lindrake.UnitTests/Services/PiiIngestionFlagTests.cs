using OmniSift.Api.Services;

namespace OmniSift.UnitTests.Services;

public sealed class PiiIngestionFlagTests
{
    [Fact]
    public void PiiScanner_DetectsSsn()
    {
        var matches = PiiScanner.Scan("SSN is 123-45-6789");
        Assert.Contains(matches, m => m.Type == PiiType.Ssn);
    }

    [Fact]
    public void PiiScanner_CleanText_ReturnsEmpty()
    {
        var matches = PiiScanner.Scan("Hello world, no PII here.");
        Assert.Empty(matches);
    }

    [Theory]
    [InlineData("SSN is 123-45-6789", true)]
    [InlineData("No sensitive data here.", false)]
    public void HasPiiFlag_MatchesScanResult(string text, bool expectedHasPii)
    {
        var matches = PiiScanner.Scan(text);
        var hasPii = matches.Count > 0;
        Assert.Equal(expectedHasPii, hasPii);
    }
}
