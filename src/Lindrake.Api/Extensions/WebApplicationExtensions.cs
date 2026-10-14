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

        // Gives every body-less error response a ProblemDetails body (via the
        // AddProblemDetails service): bearer 401 challenges (WWW-Authenticate kept),
        // policy 403s, rate-limit 429s and unmatched-route 404s. Must wrap
        // UseAuthentication/UseAuthorization. Responses that already carry a body or
        // have started (the agent SSE stream, /health) are left alone.
        app.UseStatusCodePages();

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

        app.UseAuthentication();
        app.UseAuthorization();

        // After authentication: the per-tenant policy partitions on the tenant_id
        // claim, so ctx.User must already be populated (before auth every caller
        // shared one "anonymous" bucket).
        app.UseRateLimiter();

        // Tenant resolution middleware — binds the scope to the validated JWT's
        // tenant_id claim (the RLS session variable is set per connection open by
        // TenantConnectionInterceptor). Runs after auth so the
        // ClaimsPrincipal is populated. The legacy X-Tenant-Id/X-API-Key header path
        // was removed: it allowed tenant spoofing.
        app.UseTenantMiddleware();

        // Default policy (tenant-scoped identity) on every controller; [AllowAnonymous] opts out.
        app.MapControllers().RequireAuthorization();
        app.MapHealthChecks("/health").AllowAnonymous();

        return app;
    }
}
