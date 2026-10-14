using System.Text.RegularExpressions;

namespace OmniSift.Api.Services;

public enum PiiType { Ssn, CreditCard, Dob }

public sealed record PiiMatch(PiiType Type, string Value);

public static partial class PiiScanner
{
    // SSN: 3 digits, optional separator, 2 digits, optional separator, 4 digits
    [GeneratedRegex(@"\b(\d{3}[-\s]?\d{2}[-\s]?\d{4})\b")]
    private static partial Regex SsnRegex();

    // Credit card: 13-19 digits, optionally grouped by spaces or dashes
    [GeneratedRegex(@"\b(\d{4}[-\s]?\d{4}[-\s]?\d{4}[-\s]?\d{1,4}(?:[-\s]?\d{1,4})?)\b")]
    private static partial Regex CreditCardRegex();

    // DOB: MM/DD/YYYY or YYYY-MM-DD
    [GeneratedRegex(@"\b(\d{2}/\d{2}/\d{4}|\d{4}-\d{2}-\d{2})\b")]
    private static partial Regex DobRegex();

    public static IReadOnlyList<PiiMatch> Scan(string text)
    {
        var results = new List<PiiMatch>();

        foreach (Match m in SsnRegex().Matches(text))
            results.Add(new PiiMatch(PiiType.Ssn, m.Value));

        foreach (Match m in CreditCardRegex().Matches(text))
        {
            // Exclude SSN matches (9 digits) that might overlap
            var digits = Regex.Replace(m.Value, @"[^\d]", "");
            if (digits.Length >= 13)
                results.Add(new PiiMatch(PiiType.CreditCard, m.Value));
        }

        foreach (Match m in DobRegex().Matches(text))
            results.Add(new PiiMatch(PiiType.Dob, m.Value));

        return results;
    }
}
