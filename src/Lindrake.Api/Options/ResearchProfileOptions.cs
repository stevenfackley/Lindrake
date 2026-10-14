// ============================================================
// Lindrake.Api — Research profile configuration
// ============================================================

using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Lindrake.Api.Plugins;
using Lindrake.Api.Services.Agent;
using Microsoft.Extensions.Options;

namespace Lindrake.Api.Options;

/// <summary>
/// The research profile the agent runs with: its system prompt, which kernel
/// plugins it may call and how reports are titled. Bound from the
/// "ResearchProfile" section; every unset value falls back to
/// <see cref="ResearchProfiles.FamilyHistory"/>, so an unconfigured deployment
/// behaves exactly as before profiles existed.
/// </summary>
public sealed class ResearchProfileOptions
{
    public const string Section = "ResearchProfile";

    /// <summary>Profile name, for logs and the <c>{profile}</c> report-title token.</summary>
    [Required]
    public string Name { get; set; } = ResearchProfiles.FamilyHistory.Name;

    /// <summary>System prompt sent at the start of every agent conversation.</summary>
    [Required]
    public string SystemPrompt { get; set; } = ResearchProfiles.FamilyHistory.SystemPrompt;

    /// <summary>
    /// Plugins imported into the agent's kernel (names from <see cref="AgentPlugins"/>).
    /// Null — the key is absent — means all of them. Kept nullable rather than
    /// defaulted to a list because the configuration binder appends to, rather
    /// than replaces, a pre-populated list.
    /// </summary>
    public List<string>? EnabledPlugins { get; set; }

    /// <summary>
    /// Title of a generated report when the request names none. Tokens:
    /// <c>{profile}</c> (<see cref="Name"/>) and <c>{date}</c> (UTC, yyyy-MM-dd).
    /// </summary>
    [Required]
    public string ReportTitle { get; set; } = ResearchProfiles.FamilyHistory.ReportTitle;

    /// <summary>The plugins to load: <see cref="EnabledPlugins"/>, or all when unset.</summary>
    public IReadOnlyList<string> ResolvedPlugins => EnabledPlugins ?? AgentPlugins.All;

    /// <summary><see cref="ReportTitle"/> with its tokens expanded.</summary>
    public string FormatReportTitle(DateTimeOffset utcNow) => ReportTitle
        .Replace("{profile}", Name, StringComparison.Ordinal)
        .Replace("{date}", utcNow.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), StringComparison.Ordinal);
}

/// <summary>
/// Startup guard: <see cref="ResearchProfileOptions.EnabledPlugins"/> may only
/// name known plugins, each once.
/// </summary>
internal sealed class ResearchProfileOptionsValidator : IValidateOptions<ResearchProfileOptions>
{
    public ValidateOptionsResult Validate(string? name, ResearchProfileOptions options)
    {
        if (options.EnabledPlugins is not { } plugins)
            return ValidateOptionsResult.Success;

        var failures = new List<string>();

        var unknown = plugins.Where(p => !AgentPlugins.All.Contains(p, StringComparer.Ordinal)).ToList();
        if (unknown.Count > 0)
        {
            failures.Add(
                $"ResearchProfile:EnabledPlugins contains unknown plugin(s): {string.Join(", ", unknown)}. " +
                $"Known plugins: {string.Join(", ", AgentPlugins.All)}.");
        }

        var duplicates = plugins.GroupBy(p => p, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicates.Count > 0)
            failures.Add($"ResearchProfile:EnabledPlugins lists plugin(s) more than once: {string.Join(", ", duplicates)}.");

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
