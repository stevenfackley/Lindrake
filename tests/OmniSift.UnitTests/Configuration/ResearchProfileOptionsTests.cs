// ============================================================
// Unit Tests — ResearchProfile options and the kernel it shapes
// ============================================================

using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Moq;
using OmniSift.Api.Data;
using OmniSift.Api.Extensions;
using OmniSift.Api.Middleware;
using OmniSift.Api.Options;
using OmniSift.Api.Plugins;
using OmniSift.Api.Services;
using OmniSift.Api.Services.Agent;
using OmniSift.UnitTests.TestSupport;

namespace OmniSift.UnitTests.Configuration;

public sealed class ResearchProfileOptionsTests
{
    [Fact]
    public void Defaults_AreTheFamilyHistoryProfile()
    {
        var options = Resolve([]);

        options.Name.Should().Be(ResearchProfiles.FamilyHistory.Name);
        options.SystemPrompt.Should().Be(ResearchProfiles.FamilyHistory.SystemPrompt);
        options.ResolvedPlugins.Should().Equal(AgentPlugins.VectorSearch, AgentPlugins.WebScraper, AgentPlugins.WaybackMachine);
        options.FormatReportTitle(DateTimeOffset.UnixEpoch).Should().Be("Research Report");
    }

    [Fact]
    public void Kernel_WithoutConfiguration_LoadsAllPlugins()
    {
        PluginNames([]).Should().Equal(AgentPlugins.All);
    }

    [Fact]
    public void Kernel_ProfileWithoutWebScraper_DoesNotLoadIt()
    {
        var plugins = PluginNames(new()
        {
            ["ResearchProfile:EnabledPlugins:0"] = AgentPlugins.VectorSearch,
            ["ResearchProfile:EnabledPlugins:1"] = AgentPlugins.WaybackMachine,
        });

        plugins.Should().Equal(AgentPlugins.VectorSearch, AgentPlugins.WaybackMachine);
    }

    [Fact]
    public void Validation_RejectsUnknownPluginNames()
    {
        var act = () => Resolve(new() { ["ResearchProfile:EnabledPlugins:0"] = "ShellExec" });

        act.Should().Throw<OptionsValidationException>().WithMessage("*unknown plugin(s): ShellExec*");
    }

    [Fact]
    public void Validation_RejectsDuplicatePlugins()
    {
        var act = () => Resolve(new()
        {
            ["ResearchProfile:EnabledPlugins:0"] = AgentPlugins.VectorSearch,
            ["ResearchProfile:EnabledPlugins:1"] = AgentPlugins.VectorSearch,
        });

        act.Should().Throw<OptionsValidationException>().WithMessage("*more than once: VectorSearch*");
    }

    [Fact]
    public void Validation_RejectsBlankSystemPrompt()
    {
        var act = () => Resolve(new() { ["ResearchProfile:SystemPrompt"] = "  " });

        act.Should().Throw<OptionsValidationException>().WithMessage("*SystemPrompt*");
    }

    [Fact]
    public void FormatReportTitle_ExpandsProfileAndDateTokens()
    {
        var options = new ResearchProfileOptions { Name = "Claims", ReportTitle = "{profile} file review {date}" };

        options.FormatReportTitle(new DateTimeOffset(2026, 10, 14, 23, 30, 0, TimeSpan.FromHours(-5)))
            .Should().Be("Claims file review 2026-10-15");
    }

    private static ResearchProfileOptions Resolve(Dictionary<string, string?> settings)
    {
        using var sp = BuildServices(settings);
        return sp.GetRequiredService<IOptions<ResearchProfileOptions>>().Value;
    }

    private static List<string> PluginNames(Dictionary<string, string?> settings)
    {
        using var sp = BuildServices(settings);
        using var scope = sp.CreateScope();
        return [.. scope.ServiceProvider.GetRequiredService<Kernel>().Plugins.Select(p => p.Name)];
    }

    private static ServiceProvider BuildServices(Dictionary<string, string?> settings)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var databaseName = "profile-" + Guid.NewGuid();

        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IConfiguration>(config)
            .AddScoped<OmniSiftDbContext>(sp => InMemoryDb.Create(databaseName, sp.GetRequiredService<ITenantContext>()))
            .AddScoped<ITenantContext, TenantContext>()
            .AddSingleton(Mock.Of<IEmbeddingService>());
        services.AddOmniSiftAgent();

        return services.BuildServiceProvider();
    }
}
