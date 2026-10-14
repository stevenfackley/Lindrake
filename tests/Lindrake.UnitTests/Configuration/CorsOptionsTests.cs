// ============================================================
// Unit Tests — CORS origins
// The default policy must allow exactly the configured origins:
// no hard-coded localhost entries outside Development.
// ============================================================

using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OmniSift.Api.Extensions;
using CorsPolicyOptions = Microsoft.AspNetCore.Cors.Infrastructure.CorsOptions;

namespace OmniSift.UnitTests.Configuration;

public sealed class CorsOptionsTests
{
    [Fact]
    public void DefaultPolicy_ConfiguredOrigins_AreTheOnlyOrigins()
    {
        var origins = DefaultPolicyOrigins(new Dictionary<string, string?>
        {
            ["Cors:AllowedOrigins:0"] = "https://app.example.com"
        });

        origins.Should().Equal("https://app.example.com");
    }

    [Fact]
    public void DefaultPolicy_NothingConfigured_AllowsNoOrigin()
    {
        DefaultPolicyOrigins([]).Should().BeEmpty();
    }

    [Fact]
    public void ProductionAppSettings_DoNotAllowLocalhost()
    {
        var appSettings = FindApiFile("appsettings.json");
        var origins = new ConfigurationBuilder().AddJsonFile(appSettings).Build()
            .GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

        origins.Should().NotContain(o => o.Contains("localhost"));
    }

    private static IList<string> DefaultPolicyOrigins(Dictionary<string, string?> settings)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        using var sp = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IConfiguration>(config)
            .AddOmniSiftApi()
            .BuildServiceProvider();

        var cors = sp.GetRequiredService<IOptions<CorsPolicyOptions>>().Value;
        return cors.GetPolicy(cors.DefaultPolicyName)!.Origins;
    }

    private static string FindApiFile(string name)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "src", "Lindrake.Api", name);
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException($"Could not locate src/Lindrake.Api/{name}.");
    }
}
