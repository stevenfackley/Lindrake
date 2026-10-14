// ============================================================
// OmniSift.Api — Service registration modules
// One extension per concern; Program.cs composes them.
// ============================================================

using System.Net.Http.Headers;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.SemanticKernel;
using OmniSift.Api.Data;
using OmniSift.Api.Infrastructure;
using OmniSift.Api.Middleware;
using OmniSift.Api.Options;
using OmniSift.Api.Plugins;
using OmniSift.Api.Services;
using OmniSift.Shared;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Events;
using CorsPolicyOptions = Microsoft.AspNetCore.Cors.Infrastructure.CorsOptions;

namespace OmniSift.Api.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>Named HttpClient used by the Semantic Kernel chat connector.</summary>
    public const string AnthropicChatClient = "AnthropicChat";

    /// <summary>
    /// Binds <typeparamref name="TOptions"/> to <paramref name="section"/> and validates
    /// its data annotations when the host starts.
    /// </summary>
    private static OptionsBuilder<TOptions> AddValidatedOptions<TOptions>(
        this IServiceCollection services, string section)
        where TOptions : class =>
        services.AddOptions<TOptions>()
            .BindConfiguration(section)
            .ValidateDataAnnotations()
            .ValidateOnStart();

    /// <summary>Serilog (JSON console) + OpenTelemetry tracing and metrics.</summary>
    public static IServiceCollection AddOmniSiftObservability(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        // Replaces the bootstrap logger. JSON console output for structured
        // ingestion; CorrelationId enriched from LogContext.
        // Skipped in Testing — the bootstrap ReloadableLogger is a process-level
        // static and freezes on first host build; subsequent factory instances
        // in integration tests would fail with "logger already frozen".
        if (!environment.IsTesting())
        {
            services.AddSerilog((sp, cfg) => cfg
                .ReadFrom.Configuration(configuration)
                .ReadFrom.Services(sp)
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning)
                .MinimumLevel.Override("System", LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .Enrich.WithCorrelationIdHeader(CorrelationIdMiddleware.HeaderName)
                .WriteTo.Console(new Serilog.Formatting.Compact.CompactJsonFormatter()));
        }

        var otlpEndpoint = configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        var useOtlpExporter = !string.IsNullOrWhiteSpace(otlpEndpoint);
        var useConsoleExporter = environment.IsDevelopment() || !useOtlpExporter;

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService("OmniSift.Api"))
            .WithTracing(tracing =>
            {
                tracing
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation();

                if (useConsoleExporter)
                    tracing.AddConsoleExporter();

                if (useOtlpExporter)
                    tracing.AddOtlpExporter();
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation();

                if (useConsoleExporter)
                    metrics.AddConsoleExporter();

                if (useOtlpExporter)
                    metrics.AddOtlpExporter();
            });

        return services;
    }

    /// <summary>EF Core (Npgsql + pgvector), tenant context and audit logging.</summary>
    public static IServiceCollection AddOmniSiftPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<OmniSiftDbContext>(options =>
            options.UseNpgsql(
                configuration.GetConnectionString("DefaultConnection"),
                npgsql =>
                {
                    npgsql.UseVector();
                    npgsql.EnableRetryOnFailure(3);
                }));

        services.AddHttpContextAccessor();
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped<IAuditLogger, AuditLogger>();

        return services;
    }

    /// <summary>
    /// Embedding provider (local ONNX or OpenAI). The provider is chosen per
    /// resolution from <see cref="EmbeddingOptions.Provider"/>, so neither
    /// backend is constructed unless selected.
    /// </summary>
    public static IServiceCollection AddOmniSiftEmbeddings(this IServiceCollection services)
    {
        services.AddValidatedOptions<EmbeddingOptions>(EmbeddingOptions.Section);
        services.AddValidatedOptions<OpenAiOptions>(OpenAiOptions.Section);
        services.AddSingleton<IValidateOptions<EmbeddingOptions>, EmbeddingOptionsValidator>();

        // One thread-safe InferenceSession shared process-wide. The constructor
        // fails fast (throws) if the model/vocab are missing or the model width
        // disagrees with the pgvector column.
        services.AddSingleton<OnnxEmbeddingService>();

        // OpenAI embedding client — with resilience pipeline.
        services.AddHttpClient<OpenAIEmbeddingService>((sp, client) =>
        {
            client.BaseAddress = OpenAIEmbeddingService.BaseAddress;
            client.SetBearerToken(sp.GetRequiredService<IOptions<OpenAiOptions>>().Value.ApiKey);
            client.Timeout = TimeSpan.FromSeconds(60);
        })
        .AddStandardResilienceHandler();

        services.AddTransient<IEmbeddingService>(sp =>
            sp.GetRequiredService<IOptions<EmbeddingOptions>>().Value.Provider == EmbeddingProvider.Onnx
                ? sp.GetRequiredService<OnnxEmbeddingService>()
                : sp.GetRequiredService<OpenAIEmbeddingService>());

        return services;
    }

    /// <summary>Text extraction, chunking and the async ingestion pipeline.</summary>
    public static IServiceCollection AddOmniSiftIngestion(this IServiceCollection services)
    {
        // Default IHttpClientFactory client used by web ingestion.
        services.AddHttpClient();

        services.AddSingleton<ITextChunker, TextChunker>();

        // Keyed DI: each extractor registered by its source-type key.
        // DocumentIngestionService resolves via [FromKeyedServices] — no runtime scan.
        services.AddKeyedScoped<ITextExtractor, PdfTextExtractor>("pdf");
        services.AddKeyedScoped<ITextExtractor, SmsTextExtractor>("sms");
        services.AddKeyedScoped<ITextExtractor, WebTextExtractor>("web");
        services.AddKeyedScoped<ITextExtractor, DocxTextExtractor>("docx");
        services.AddKeyedScoped<ITextExtractor, EmailTextExtractor>("email");

        services.AddScoped<IDocumentIngestionService, DocumentIngestionService>();

        // Async ingestion pipeline via System.Threading.Channels.
        // IngestionChannel is the shared queue (singleton).
        // IngestionBackgroundService drains it in a hosted background loop.
        // BackgroundIngestionService runs the actual extract→chunk→embed work
        // in a fresh DI scope per item (with RLS re-established from the work item tenantId).
        services.AddSingleton<IngestionChannel>();
        services.AddScoped<IBackgroundIngestionService, BackgroundIngestionService>();
        services.AddHostedService<IngestionBackgroundService>();

        return services;
    }

    /// <summary>Semantic Kernel, its plugins and the LLM-backed services.</summary>
    public static IServiceCollection AddOmniSiftAgent(this IServiceCollection services)
    {
        services.AddValidatedOptions<AnthropicOptions>(AnthropicOptions.Section);
        services.AddValidatedOptions<TavilyOptions>(TavilyOptions.Section);

        services.AddScoped<ICitationAccumulator, CitationAccumulator>();
        services.AddScoped<IEntityExtractionService, EntityExtractionService>();

        // Named HttpClient for the Anthropic-compatible endpoint (avoids leaked HttpClient)
        services.AddHttpClient(AnthropicChatClient, client =>
        {
            client.BaseAddress = new Uri("https://api.anthropic.com/v1/");
        })
        .AddStandardResilienceHandler(options =>
        {
            options.Retry.MaxRetryAttempts = 3;
            options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(120);
        });

        // Build a base Kernel once and store it as a singleton Func factory.
        services.AddSingleton<Func<Kernel>>(sp =>
        {
            var anthropicOpts = sp.GetRequiredService<IOptions<AnthropicOptions>>().Value;
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();

            return () =>
            {
                var kernelBuilder = Kernel.CreateBuilder();

                if (!string.IsNullOrWhiteSpace(anthropicOpts.ApiKey))
                {
                    kernelBuilder.AddOpenAIChatCompletion(
                        modelId: anthropicOpts.ModelId,
                        apiKey: anthropicOpts.ApiKey,
                        httpClient: httpClientFactory.CreateClient(AnthropicChatClient));
                }

                return kernelBuilder.Build();
            };
        });

        // Plugins — WebScraper and WaybackMachine use typed HttpClients with resilience
        services.AddHttpClient<WebScraperPlugin>((sp, client) =>
        {
            client.BaseAddress = WebScraperPlugin.TavilyBaseAddress;
            client.SetBearerToken(sp.GetRequiredService<IOptions<TavilyOptions>>().Value.ApiKey);
        })
        .AddStandardResilienceHandler();

        services.AddHttpClient<WaybackMachinePlugin>(client =>
            client.BaseAddress = WaybackMachinePlugin.BaseAddress)
        .AddStandardResilienceHandler();

        services.AddScoped<VectorSearchPlugin>();

        // Register a scoped Kernel that includes plugins for each request.
        services.AddScoped(sp =>
        {
            var kernel = sp.GetRequiredService<Func<Kernel>>()();

            kernel.ImportPluginFromObject(sp.GetRequiredService<VectorSearchPlugin>(), "VectorSearch");
            kernel.ImportPluginFromObject(sp.GetRequiredService<WebScraperPlugin>(), "WebScraper");
            kernel.ImportPluginFromObject(sp.GetRequiredService<WaybackMachinePlugin>(), "WaybackMachine");

            return kernel;
        });

        return services;
    }

    /// <summary>
    /// JWT bearer authentication.
    /// Production: validate Supabase-issued ES256/RS256 access tokens via OIDC
    /// discovery (JWKS) — NO symmetric secret. Enabled by setting Supabase:Url.
    /// The Supabase project must emit a custom <c>tenant_id</c> claim (app_metadata /
    /// access-token hook); tenant identity is read from that claim, never a header.
    /// Development fallback: when Supabase:Url is empty, the legacy in-house HS256
    /// tokens issued by AuthController (Jwt:Secret) are validated instead. That
    /// fallback refuses to start outside Development/Testing (JwtOptionsValidator),
    /// and AuthController only exists while it is active.
    /// </summary>
    public static IServiceCollection AddOmniSiftAuth(this IServiceCollection services)
    {
        services.AddValidatedOptions<JwtOptions>(JwtOptions.Section);
        services.AddValidatedOptions<SupabaseOptions>(SupabaseOptions.Section);
        services.AddSingleton<IValidateOptions<JwtOptions>, JwtOptionsValidator>();

        services.AddOptions<MvcOptions>()
            .Configure<IOptions<SupabaseOptions>>((mvc, supabase) =>
                mvc.Conventions.Add(new LocalIdentityProviderConvention(enabled: !supabase.Value.IsConfigured)));

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<SupabaseOptions>, IOptions<JwtOptions>, IHostEnvironment>(
                (options, supabase, jwt, environment) =>
                    ConfigureJwtBearer(options, supabase.Value, jwt.Value, environment));
        services.AddAuthorization();

        services.AddSingleton<IPasswordHasher, PasswordHasherService>();
        services.AddSingleton<JwtTokenService>();

        return services;
    }

    /// <summary>Sets <c>Authorization: Bearer</c> unless the key is unset.</summary>
    private static void SetBearerToken(this HttpClient client, string apiKey)
    {
        if (!string.IsNullOrWhiteSpace(apiKey))
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }

    private static void ConfigureJwtBearer(
        JwtBearerOptions options,
        SupabaseOptions supabase,
        JwtOptions jwt,
        IHostEnvironment environment)
    {
        // Preserve raw JWT claim names ("sub", "tenant_id") instead of
        // remapping them to legacy SOAP URIs — downstream reads them directly.
        options.MapInboundClaims = false;

        if (supabase.IsConfigured)
        {
            var supabaseAuthBase = supabase.Url.TrimEnd('/') + "/auth/v1";

            // MetadataAddress makes JwtBearer build its own ConfigurationManager,
            // fetch the JWKS, match the ES256 key by kid, refresh on rotation,
            // and supply ValidIssuer from the discovery document — no custom keys.
            options.MetadataAddress = $"{supabaseAuthBase}/.well-known/openid-configuration";
            options.RequireHttpsMetadata = !environment.IsDevelopment();
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidAudience = supabase.Audience,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true
            };
            return;
        }

        // Legacy / development: in-house HS256 self-issued tokens.
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(jwt.Secret))
        };
    }

    /// <summary>Per-tenant token-bucket rate limiting.</summary>
    public static IServiceCollection AddOmniSiftRateLimiting(this IServiceCollection services)
    {
        services.AddValidatedOptions<RateLimitingOptions>(RateLimitingOptions.Section);

        services.AddRateLimiter(_ => { });
        services.AddOptions<RateLimiterOptions>()
            .Configure<IOptions<RateLimitingOptions>>((options, limits) =>
            {
                var bucket = limits.Value;

                // Partition by the authenticated tenant claim, never a client header.
                // Requires UseRateLimiter() to run after UseAuthentication().
                options.AddPolicy(RateLimitPolicies.PerTenant, ctx =>
                    RateLimitPartition.GetTokenBucketLimiter(
                        partitionKey: ctx.User.TryGetTenantId()?.ToString() ?? "anonymous",
                        factory: _ => new TokenBucketRateLimiterOptions
                        {
                            TokenLimit = bucket.TokenLimit,
                            ReplenishmentPeriod = bucket.ReplenishmentPeriod,
                            TokensPerPeriod = bucket.TokensPerPeriod,
                            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                            QueueLimit = 0
                        }));

                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            });

        return services;
    }

    /// <summary>Controllers, ProblemDetails, health checks, Swagger, JSON and CORS.</summary>
    public static IServiceCollection AddOmniSiftApi(this IServiceCollection services)
    {
        services.AddValidatedOptions<Options.CorsOptions>(Options.CorsOptions.Section);

        services.AddProblemDetails();
        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddHealthChecks();

        // Source-generated metadata for the shared DTOs; types outside the context
        // (ProblemDetails, anonymous objects) fall through to the reflection resolver.
        services.AddControllers()
            .AddJsonOptions(opts =>
                opts.JsonSerializerOptions.TypeInfoResolverChain.Insert(0, OmniSiftJsonContext.Default));
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new()
            {
                Title = "OmniSift API",
                Version = "v1",
                Description = "Multi-tenant AI research agent API with document ingestion and semantic search."
            });
        });

        // Same for Results/WriteAsJsonAsync/ProblemDetails writer (HTTP JSON options;
        // MVC ignores these, hence AddJsonOptions above).
        services.ConfigureHttpJsonOptions(opts =>
            opts.SerializerOptions.TypeInfoResolverChain.Insert(0, OmniSiftJsonContext.Default));

        // CORS (allow Blazor frontend) — origins from strongly-typed options.
        services.AddCors();
        services.AddOptions<CorsPolicyOptions>()
            .Configure<IOptions<Options.CorsOptions>>((cors, origins) =>
                cors.AddDefaultPolicy(policy => policy
                    .WithOrigins(origins.Value.AllowedOrigins)
                    .AllowAnyHeader()
                    .AllowAnyMethod()));

        return services;
    }
}
