// ============================================================
// OmniSift.Api — Application Entry Point
// Composes the service modules in Extensions/ and runs the host.
// ============================================================

using OmniSift.Api.Extensions;
using QuestPDF.Infrastructure;
using Serilog;
using Serilog.Events;

// ── Serilog Bootstrap Logger ─────────────────────────────────
// Used only during host startup (before DI is ready). Replaced
// by the full logger configured in AddOmniSiftObservability().
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    QuestPDF.Settings.License = LicenseType.Community;

    var builder = WebApplication.CreateBuilder(args);

    // In production, docker-compose.prod.yml mounts secrets as files under /run/secrets/.
    // AddKeyPerFile reads each file as a config key, converting __ to : (e.g.
    // Anthropic__ApiKey → Anthropic:ApiKey). Added after env vars so secrets win.
    builder.Configuration.AddKeyPerFile(directoryPath: "/run/secrets", optional: true);

    builder.Services
        .AddOmniSiftObservability(builder.Configuration, builder.Environment)
        .AddOmniSiftPersistence(builder.Configuration)
        .AddOmniSiftEmbeddings()
        .AddOmniSiftIngestion()
        .AddOmniSiftAgent()
        .AddOmniSiftAuth()
        .AddOmniSiftRateLimiting()
        .AddOmniSiftApi();

    var app = builder.Build();

    app.InitializeOmniSift()
        .UseOmniSiftPipeline()
        .Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "OmniSift host terminated unexpectedly");
    Environment.ExitCode = 1;
}
finally
{
    Log.CloseAndFlush();
}

// Make Program accessible for integration tests
public partial class Program { }
