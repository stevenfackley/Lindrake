// ============================================================
// Lindrake.Api — Wayback Machine Plugin for Semantic Kernel
// Retrieves archived web page snapshots from the Internet Archive
// ============================================================

using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lindrake.Api.Services;
using Microsoft.SemanticKernel;

namespace Lindrake.Api.Plugins;

/// <summary>
/// Semantic Kernel plugin that queries the Wayback Machine API
/// to find archived snapshots of web pages. Useful for historical
/// research and recovering removed or changed web content.
/// Also records results in <see cref="ICitationAccumulator"/>.
/// The typed HttpClient carries <see cref="BaseAddress"/> (configured in
/// AddLindrakeAgent); requests use relative URIs.
/// </summary>
public sealed partial class WaybackMachinePlugin(
    HttpClient httpClient,
    ICitationAccumulator citations,
    ILogger<WaybackMachinePlugin> logger)
{
    public static readonly Uri BaseAddress = new("https://archive.org/");

    [KernelFunction("GetArchivedPage")]
    [Description("Checks the Internet Archive's Wayback Machine for an archived snapshot of a web page. " +
                 "Returns the closest archived snapshot URL and timestamp if available. " +
                 "Use this when the user needs to find historical versions of web pages or recover removed content.")]
    public async Task<string> GetArchivedPageAsync(
        [Description("The URL of the web page to look up in the Wayback Machine")] string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return "No URL provided for Wayback Machine lookup.";

        logger.LogDebug("WaybackMachine: looking up {Url}", url);

        try
        {
            var encodedUrl = Uri.EscapeDataString(url);
            var response = await httpClient.GetFromJsonAsync(
                $"wayback/available?url={encodedUrl}",
                WaybackJsonContext.Default.WaybackResponse).ConfigureAwait(false);

            if (response?.ArchivedSnapshots?.Closest is null)
            {
                logger.LogInformation("No Wayback Machine snapshot found for {Url}", url);
                return JsonSerializer.Serialize(
                    new SnapshotNotFound(
                        Found: false,
                        OriginalUrl: url,
                        Message: "No archived snapshot found for this URL in the Wayback Machine."),
                    WaybackJsonContext.Default.SnapshotNotFound);
            }

            var snapshot = response.ArchivedSnapshots.Closest;

            logger.LogInformation(
                "Wayback Machine snapshot found for {Url}: {SnapshotUrl} ({Timestamp})",
                url, snapshot.Url, snapshot.Timestamp);

            // Record citation
            citations.AddArchivedPage(snapshot.Url, url, snapshot.Timestamp);

            return JsonSerializer.Serialize(
                new SnapshotFound(
                    Found: true,
                    OriginalUrl: url,
                    ArchiveUrl: snapshot.Url,
                    Timestamp: snapshot.Timestamp,
                    Available: snapshot.Available,
                    Status: snapshot.Status),
                WaybackJsonContext.Default.SnapshotFound);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "WaybackMachine lookup failed for {Url}", url);
            return $"Error querying Wayback Machine: {ex.Message}";
        }
    }

    // ── Wayback Machine API DTOs ─────────────────────

    [JsonSerializable(typeof(WaybackResponse))]
    [JsonSerializable(typeof(SnapshotFound))]
    [JsonSerializable(typeof(SnapshotNotFound))]
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
    private sealed partial class WaybackJsonContext : JsonSerializerContext;

    /// <summary>Tool output handed back to the LLM.</summary>
    private sealed record SnapshotFound(
        bool Found, string OriginalUrl, string ArchiveUrl, string Timestamp, bool Available, string Status);

    private sealed record SnapshotNotFound(bool Found, string OriginalUrl, string Message);

    private sealed class WaybackResponse
    {
        [JsonPropertyName("archived_snapshots")]
        public ArchivedSnapshots? ArchivedSnapshots { get; init; }
    }

    private sealed class ArchivedSnapshots
    {
        [JsonPropertyName("closest")]
        public WaybackSnapshot? Closest { get; init; }
    }

    private sealed class WaybackSnapshot
    {
        [JsonPropertyName("url")]
        public string Url { get; init; } = string.Empty;

        [JsonPropertyName("timestamp")]
        public string Timestamp { get; init; } = string.Empty;

        [JsonPropertyName("available")]
        public bool Available { get; init; }

        [JsonPropertyName("status")]
        public string Status { get; init; } = string.Empty;
    }
}
