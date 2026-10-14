using System.Text.RegularExpressions;
using FluentAssertions;

namespace OmniSift.UnitTests.Web;

/// <summary>Third-party scripts in the Blazor host page must be pinned with Subresource Integrity.</summary>
public sealed partial class IndexHtmlTests
{
    [Fact]
    public void ExternalScripts_ArePinnedWithIntegrity()
    {
        var html = File.ReadAllText(Path.Combine(RepoRoot(), "src", "OmniSift.Web", "wwwroot", "index.html"));

        var external = ScriptTag().Matches(html)
            .Select(m => m.Value)
            .Where(tag => tag.Contains("src=\"http", StringComparison.Ordinal))
            .ToList();

        external.Should().NotBeEmpty();
        external.Should().AllSatisfy(tag =>
        {
            tag.Should().MatchRegex("integrity=\"sha(384|512)-[A-Za-z0-9+/=]+\"");
            tag.Should().Contain("crossorigin=\"anonymous\"");
            tag.Should().NotContain("@latest").And.NotContain("unpkg.com");
        });
    }

    [GeneratedRegex("<script\\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex ScriptTag();

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "OmniSift.sln")))
                return dir.FullName;
        }

        throw new InvalidOperationException("Could not locate OmniSift.sln above " + AppContext.BaseDirectory);
    }
}
