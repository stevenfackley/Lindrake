// ============================================================
// Lindrake.Api — HTML to plain text (HtmlAgilityPack)
// Shared by the web and email extractors.
// ============================================================

using HtmlAgilityPack;

namespace Lindrake.Api.Services;

internal static class HtmlText
{
    /// <summary>Removes every element named in <paramref name="tags"/> (with its content) and all comments.</summary>
    public static void RemoveNodes(HtmlDocument doc, IEnumerable<string> tags)
    {
        var nodesToRemove = new List<HtmlNode>();

        foreach (var tag in tags)
        {
            var nodes = doc.DocumentNode.SelectNodes($"//{tag}");
            if (nodes is not null)
                nodesToRemove.AddRange(nodes);
        }

        var comments = doc.DocumentNode.SelectNodes("//comment()");
        if (comments is not null)
            nodesToRemove.AddRange(comments);

        foreach (var node in nodesToRemove)
            node.Remove();
    }

    /// <summary>Decoded inner text with blank lines dropped and each line trimmed.</summary>
    public static string CleanText(HtmlNode node)
    {
        var text = HtmlEntity.DeEntitize(node.InnerText);

        var lines = text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0);

        return string.Join("\n", lines);
    }
}
