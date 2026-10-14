using FluentAssertions;
using OmniSift.Api.Services;

namespace OmniSift.UnitTests.Services;

public sealed class PiiScannerTests
{
    [Fact]
    public void Scan_Ssn_Detected()
    {
        var matches = PiiScanner.Scan("SSN: 123-45-6789");

        matches.Should().ContainSingle(m => m.Type == PiiType.Ssn && m.Value.Contains("123-45-6789"));
    }

    [Fact]
    public void Scan_CreditCard_Detected()
    {
        var matches = PiiScanner.Scan("Card: 4111 1111 1111 1111");

        matches.Should().Contain(m => m.Type == PiiType.CreditCard);
    }

    [Fact]
    public void Scan_Dob_Detected()
    {
        var matches = PiiScanner.Scan("DOB: 01/15/1985");

        matches.Should().ContainSingle(m => m.Type == PiiType.Dob && m.Value == "01/15/1985");
    }

    [Fact]
    public void Scan_PlainText_ReturnsEmpty()
    {
        var matches = PiiScanner.Scan("Hello world, nothing sensitive here.");

        matches.Should().BeEmpty();
    }

    [Fact]
    public void Scan_IsoDateDob_Detected()
    {
        var matches = PiiScanner.Scan("Born: 1985-01-15");

        matches.Should().Contain(m => m.Type == PiiType.Dob);
    }
}
