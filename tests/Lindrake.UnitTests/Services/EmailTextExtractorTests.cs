// ============================================================
// Unit Tests — EmailTextExtractor
// Constructs small EML/MBOX messages using MimeKit and verifies
// that the extractor returns subject, headers, and body text.
// ============================================================

using System.Text;
using FluentAssertions;
using Lindrake.Api.Services;
using Microsoft.Extensions.Logging;
using MimeKit;
using Moq;

namespace Lindrake.UnitTests.Services;

public sealed class EmailTextExtractorTests
{
    private readonly EmailTextExtractor _sut = new(Mock.Of<ILogger<EmailTextExtractor>>());

    [Fact]
    public void SourceType_ReturnsEmail()
    {
        _sut.SourceType.Should().Be("email");
    }

    [Fact]
    public async Task ExtractText_SingleEml_ReturnsSubjectAndBody()
    {
        using var ms = BuildEml(
            subject: "Test subject",
            from: "sender@example.com",
            to: "recipient@example.com",
            body: "This is the email body.");

        var result = await _sut.ExtractTextAsync(ms, "message.eml");

        result.Should().Contain("Test subject");
        result.Should().Contain("This is the email body.");
    }

    [Fact]
    public async Task ExtractText_EmlIncludesFromAndTo()
    {
        using var ms = BuildEml(
            subject: "Headers check",
            from: "alice@example.com",
            to: "bob@example.com",
            body: "Body text.");

        var result = await _sut.ExtractTextAsync(ms, "message.eml");

        result.Should().Contain("alice@example.com");
        result.Should().Contain("bob@example.com");
    }

    [Fact]
    public async Task ExtractText_MsgExtension_ThrowsInvalidOperation()
    {
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes("doesn't matter"));

        var act = async () => await _sut.ExtractTextAsync(ms, "outlook.msg");
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*.msg*");
    }

    [Fact]
    public async Task ExtractText_NullStream_ThrowsArgumentNull()
    {
        var act = async () => await _sut.ExtractTextAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ExtractText_HtmlOnlyBody_FallsBackToStrippedText()
    {
        // Build a message whose TextBody is empty but HtmlBody contains text
        var message = new MimeMessage();
        message.Subject = "Html only";
        message.From.Add(new MailboxAddress("Sender", "sender@example.com"));
        message.To.Add(new MailboxAddress("Recipient", "recipient@example.com"));

        var builder = new BodyBuilder
        {
            HtmlBody = "<html><body><p>Html content</p></body></html>"
        };
        message.Body = builder.ToMessageBody();

        using var ms = new MemoryStream();
        await message.WriteToAsync(ms);
        ms.Position = 0;

        var result = await _sut.ExtractTextAsync(ms, "html-only.eml");

        result.Should().Contain("Html content");
    }

    [Fact]
    public async Task ExtractText_HtmlOnlyBody_DropsScriptAndStyleContent()
    {
        var message = new MimeMessage { Subject = "Tracked" };
        message.From.Add(new MailboxAddress("Sender", "sender@example.com"));
        message.Body = new BodyBuilder
        {
            HtmlBody = "<html><head><style>p { color: red; }</style><title>t</title></head>"
                + "<body><script>track('user');</script><p>Visible &amp; kept</p></body></html>"
        }.ToMessageBody();

        using var ms = new MemoryStream();
        await message.WriteToAsync(ms);
        ms.Position = 0;

        var result = await _sut.ExtractTextAsync(ms, "tracked.eml");

        result.Should().Contain("Visible & kept");
        result.Should().NotContain("track(").And.NotContain("color: red");
    }

    [Fact]
    public async Task ExtractText_SeekableStreamNotAtStart_ReadsFromCurrentPosition()
    {
        using var eml = BuildEml("Offset", "a@example.com", "b@example.com", "Offset body.");
        using var padded = new MemoryStream();
        padded.Write("JUNK"u8);
        eml.CopyTo(padded);
        padded.Position = 4;

        var result = await _sut.ExtractTextAsync(padded, "offset.eml");

        result.Should().Contain("Offset body.");
    }

    // ── Helpers ──────────────────────────────────────────────

    /// <summary>
    /// Creates an EML stream using MimeKit's BodyBuilder.
    /// </summary>
    private static MemoryStream BuildEml(
        string subject,
        string from,
        string to,
        string body)
    {
        var message = new MimeMessage();
        message.Subject = subject;
        message.From.Add(MailboxAddress.Parse(from));
        message.To.Add(MailboxAddress.Parse(to));
        message.Date = new DateTimeOffset(2024, 1, 1, 10, 0, 0, TimeSpan.Zero);

        var builder = new BodyBuilder { TextBody = body };
        message.Body = builder.ToMessageBody();

        var ms = new MemoryStream();
        message.WriteTo(ms);
        ms.Position = 0;
        return ms;
    }
}
