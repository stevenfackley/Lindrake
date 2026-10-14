// ============================================================
// OmniSift.Api — Startup tasks and HTTP middleware pipeline
// ============================================================

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OmniSift.Api.Data;
using OmniSift.Api.Infrastructure;
using OmniSift.Api.Middleware;
using OmniSift.Api.Options;
using OmniSift.Api.Services;
using Serilog;

namespace OmniSift.Api.Extensions;

public static class WebApplicationExtensions
{
    /// <summary>
    /// Applies EF migrations and eagerly loads the ONNX embedding model.
    /// Skipped in Testing — integration tests use InMemory, which doesn't support
    /// Migrate(); seeding is handled by CustomWebApplicationFactory.CreateHost.
    /// </summary>
    public static WebApplication InitializeOmniSift(this WebApplication app)
    {
        if (app.Environment.IsTesting())
            return app;

        // Run the ValidateOnStart checks now (the host would only run them in
        // Run()) so a misconfiguration fails before the database is migrated.
        app.Services.GetService<IStartupValidator>()?.Validate();

        using var scope = app.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<OmniSiftDbContext>().Database.Migrate();

        // Fail fast: force the ONNX model to load at startup (mirrors Migrate()
        // above) so a misconfigured local provider refuses to boot rather than
        // silently failing — or silently re-billing OpenAI — on first request.
        var embedding = scope.ServiceProvider.GetRequiredService<IOptions<EmbeddingOptions>>().Value;
        if (embedding.Provider == EmbeddingProvider.Onnx)
            scope.ServiceProvider.GetRequiredService<IEmbeddingService>();

        return app;
    }

    /// <summary>Middleware pipeline and endpoint mapping.</summary>
    public static WebApplication UseOmniSiftPipeline(this WebApplication app)
    {
        app.UseExceptionHandler();

        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        // Correlation ID must be first so every subsequent log line
        // and response carries the ID.
        app.UseCorrelationId();

        // Serilog request logging replaces the default ASP.NET request
        // log; emits one structured line per request with duration,
        // status code, and CorrelationId from LogContext.
        if (!app.Environment.IsTesting())
        {
            app.UseSerilogRequestLogging(opts =>
            {
                opts.EnrichDiagnosticContext = (diag, http) =>
                {
                    diag.Set("RequestHost", http.Request.Host.Value ?? string.Empty);
                    diag.Set("RequestScheme", http.Request.Scheme ?? string.Empty);
                    if (http.Items.TryGetValue(CorrelationIdMiddleware.HeaderName, out var cid))
                        diag.Set("CorrelationId", cid?.ToString() ?? string.Empty);
                };
            });
        }

        app.UseCors();
        app.UseRateLimiter();

        app.UseAuthentication();
        app.UseAuthorization();

        // Tenant resolution middleware — derives the tenant from the validated JWT
        // (tenant_id claim) and sets the RLS session variable. Runs after auth so the
        // ClaimsPrincipal is populated. The legacy X-Tenant-Id/X-API-Key header path
        // was removed: it allowed tenant spoofing.
        app.UseTenantMiddleware();

        app.MapControllers();
        app.MapHealthChecks("/health");

        return app;
    }
}
