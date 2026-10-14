// ============================================================
// Lindrake.Api — Email Text Extractor
// Extracts text from .eml and .mbox files using MimeKit.
//
// NOTE: .msg (Outlook compound-document format) is NOT supported —
// MimeKit handles RFC-2822/MIME formats only. Callers passing .msg
// files will receive an InvalidOperationException with a clear message.
// ============================================================

using System.Text;
using HtmlAgilityPack;
using MimeKit;

namespace Lindrake.Api.Services;

/// <summary>
/// Extracts readable text from email files in EML or MBOX format.
/// Produces a structured block per message: subject, from, to, date, then body.
/// Unsupported: .msg (Outlook proprietary format) — accept eml/mbox only.
/// </summary>
public sealed class EmailTextExtractor(ILogger<EmailTextExtractor> logger) : ITextExtractor
{
    private static readonly string[] NonContentTags = ["head", "script", "style", "noscript", "template"];

    /// <inheritdoc />
    public string SourceType => SourceTypes.Email;

    /// <inheritdoc />
    public async Task<string> ExtractTextAsync(
        Stream stream,
        string? fileName = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        logger.LogDebug("Extracting text from email: {FileName}", fileName ?? "unknown");

        // Reject .msg files up-front with a clear message.
        if (fileName is not null &&
            Path.GetExtension(fileName).Equals(".msg", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The .msg format (Outlook proprietary) is not supported. " +
                "Please export the message as .eml and re-upload.");
        }

        // The mbox sniff rewinds, so the input must be seekable.
        using var buffered = await SeekableStream.BufferIfNeededAsync(stream, cancellationToken).ConfigureAwait(false);
        var input = buffered ?? stream;

        // Detect mbox vs. single EML by checking for the "From " mbox envelope line.
        var isMbox = IsMboxFormat(input);

        var messages = new List<MimeMessage>();

        if (isMbox)
        {
            // MimeKit parses mbox via MimeParser with MimeFormat.Mbox.
            // ParseMessageAsync returns the next mbox record; loop until IsEndOfStream.
            var parser = new MimeParser(input, MimeFormat.Mbox);
            while (!parser.IsEndOfStream)
            {
                cancellationToken.ThrowIfCancellationRequested();
                MimeMessage? msg;
                try
                {
                    msg = await parser.ParseMessageAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (FormatException)
                {
                    // Malformed mbox separator — stop gracefully with what we have so far
                    break;
                }

                if (msg is not null)
                {
                    messages.Add(msg);
                }
            }
        }
        else
        {
            var msg = await MimeMessage.LoadAsync(input, cancellationToken).ConfigureAwait(false);
            messages.Add(msg);
        }

        if (messages.Count == 0)
        {
            logger.LogWarning("No messages found in email file: {FileName}", fileName ?? "unknown");
            return string.Empty;
        }

        var sb = new StringBuilder();

        foreach (var message in messages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Headers
            if (!string.IsNullOrWhiteSpace(message.Subject))
                sb.AppendLine($"Subject: {message.Subject}");

            if (message.From.Count > 0)
                sb.AppendLine($"From: {message.From}");

            if (message.To.Count > 0)
                sb.AppendLine($"To: {message.To}");

            if (message.Date != default)
                sb.AppendLine($"Date: {message.Date:u}");

            sb.AppendLine();

            // Prefer plain text body; fall back to the HTML body's readable text.
            var body = message.TextBody;

            if (string.IsNullOrWhiteSpace(body) && !string.IsNullOrWhiteSpace(message.HtmlBody))
            {
                body = HtmlToText(message.HtmlBody);
            }

            if (!string.IsNullOrWhiteSpace(body))
            {
                sb.AppendLine(body.Trim());
            }

            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
        }

        var result = sb.ToString().Trim();

        logger.LogInformation(
            "Extracted {Length} chars from {MessageCount} email(s): {FileName}",
            result.Length, messages.Count, fileName ?? "unknown");

        return result;
    }

    /// <summary>
    /// Returns true if the stream, from its current position, starts with an mbox
    /// "From " envelope line. The position is restored.
    /// </summary>
    private static bool IsMboxFormat(Stream input)
    {
        var start = input.Position;
        Span<byte> peek = stackalloc byte[5];
        var read = input.ReadAtLeast(peek, peek.Length, throwOnEndOfStream: false);
        input.Position = start;

        return read == peek.Length && peek.SequenceEqual("From "u8);
    }

    /// <summary>
    /// Readable text of an HTML-only body. Script, style and head content is
    /// dropped (a character-level tag stripper kept it, so tracking scripts and
    /// CSS ended up in the index) and entities are decoded.
    /// </summary>
    private static string HtmlToText(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        HtmlText.RemoveNodes(doc, NonContentTags);
        return HtmlText.CleanText(doc.DocumentNode);
    }
}
