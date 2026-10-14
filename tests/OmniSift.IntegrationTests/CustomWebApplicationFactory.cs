// ============================================================
// Integration Tests — Custom WebApplicationFactory
// Configures in-memory database and mocked services for testing
// ============================================================

using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.SemanticKernel;
using Moq;
using OmniSift.Api.Data;
using OmniSift.Api.Middleware;
using OmniSift.Api.Models;
using OmniSift.Api.Services;
using Pgvector;

namespace OmniSift.IntegrationTests;

/// <summary>
/// Custom WebApplicationFactory that replaces PostgreSQL with an in-memory
/// database and mocks external services (embedding, LLM).
///
/// Authentication: the Testing environment leaves Supabase:Url unset, so the API
/// validates the legacy in-house HS256 tokens. Tests mint those tokens via
/// <see cref="CreateToken"/> (matching the configured Jwt:Secret) and attach them
/// as Bearer — production validates Supabase ES256 via OIDC instead.
/// </summary>
public sealed class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    /// <summary>
    /// Dev tenant ID used in tests (matches db/init.sql seed).
    /// </summary>
    public static readonly Guid TestTenantId = Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890");

    // HS256 signing parameters injected into the Testing host's configuration and
    // used to mint matching tokens. Must agree with the validator in AddOmniSiftAuth.
    public const string TestJwtSecret = "omnisift-integration-tests-hs256-signing-secret-key";
    public const string TestJwtIssuer = "OmniSift";
    public const string TestJwtAudience = "OmniSift";

    /// <summary>
    /// Extra configuration layered over the defaults below (e.g. small rate limits).
    /// Tests that need it create their own factory instead of the class fixture.
    /// </summary>
    public IReadOnlyDictionary<string, string?> ExtraSettings { get; init; } =
        new Dictionary<string, string?>();

    /// <summary>
    /// Adds services (e.g. a fake chat-completion backend) to the base Semantic Kernel.
    /// Left null, the kernel has no LLM and agent endpoints answer 503.
    /// </summary>
    public Action<IKernelBuilder>? ConfigureKernel { get; init; }

    /// <summary>Last-word service overrides for a test (runs after the defaults below).</summary>
    public Action<IServiceCollection>? ConfigureTestServices { get; init; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"] = TestJwtSecret,
                ["Jwt:Issuer"] = TestJwtIssuer,
                ["Jwt:Audience"] = TestJwtAudience,
                // Supabase:Url intentionally unset → in-house HS256 validation path.
            });
            config.AddInMemoryCollection(ExtraSettings);
        });

        builder.ConfigureServices(services =>
        {
            // Replace PostgreSQL with InMemory for integration tests.
            // EF Core 10+ does not allow multiple database providers, so we must
            // remove ALL EF/DbContext/Npgsql related services and re-register cleanly.
            // This includes internal IDbContextOptionsConfiguration<T> registrations.
            var descriptorsToRemove = services.Where(d =>
            {
                var stName = d.ServiceType.FullName ?? string.Empty;
                var stAssembly = d.ServiceType.Assembly.GetName().Name ?? string.Empty;
                var itName = d.ImplementationType?.FullName ?? string.Empty;
                var itAssembly = d.ImplementationType?.Assembly.GetName().Name ?? string.Empty;

                return stName.Contains("OmniSiftDbContext")
                    || stName.Contains("DbContextOptions")
                    || stName.Contains("IDbContextOptionsConfiguration")
                    || stAssembly.Contains("Npgsql")
                    || itAssembly.Contains("Npgsql")
                    || stAssembly.Contains("EntityFrameworkCore.Relational")
                    || itAssembly.Contains("EntityFrameworkCore.Relational");
            }).ToList();

            foreach (var descriptor in descriptorsToRemove)
                services.Remove(descriptor);

            // Re-register with InMemory only.
            // Use a factory registration so we can customize model creation
            // to ignore the Vector property (unsupported by InMemory).
            var dbName = "OmniSift_IntegrationTests_" + Guid.NewGuid();
            services.AddDbContext<OmniSiftDbContext>(
                (sp, options) => options.UseInMemoryDatabase(dbName)
                    .AddInterceptors(sp.GetRequiredService<TimestampInterceptor>()),
                contextLifetime: ServiceLifetime.Scoped,
                optionsLifetime: ServiceLifetime.Singleton);

            // Replace the DbContext registration with our test subclass
            // that ignores the Pgvector Vector column (InMemory doesn't support it).
            services.AddScoped<OmniSiftDbContext>(sp =>
                new InMemoryOmniSiftDbContext(
                    sp.GetRequiredService<DbContextOptions<OmniSiftDbContext>>(),
                    sp.GetRequiredService<ITenantContext>()));

            // Replace embedding service with a mock that returns dummy vectors
            services.RemoveAll<IEmbeddingService>();
            var embeddingMock = new Mock<IEmbeddingService>();
            embeddingMock
                .Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Vector(new float[384]));
            embeddingMock
                .Setup(e => e.GenerateEmbeddingsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((IEnumerable<string> texts, CancellationToken _) =>
                    texts.Select(_ => new Vector(new float[384])).ToList());
            services.AddSingleton(embeddingMock.Object);

            // appsettings selects the ONNX provider, whose token counter needs the
            // model files; chunk with the word heuristic instead.
            services.RemoveAll<ITokenCounter>();
            services.AddSingleton<ITokenCounter, WordHeuristicTokenCounter>();

            // Replace the base kernel: no LLM unless a test supplies one via
            // ConfigureKernel. The scoped per-request Kernel (plugins + invocation
            // filter) stays the production registration and builds on top of it.
            services.RemoveAll<Func<Kernel>>();
            services.AddSingleton<Func<Kernel>>(_ => () =>
            {
                var kernelBuilder = Kernel.CreateBuilder();
                ConfigureKernel?.Invoke(kernelBuilder);
                return kernelBuilder.Build();
            });

            ConfigureTestServices?.Invoke(services);

        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);

        // Seed the test database after the host is fully built so Serilog's
        // ReloadableLogger is not frozen prematurely by a premature BuildServiceProvider call.
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OmniSiftDbContext>();
        db.Database.EnsureCreated();

        if (!db.Tenants.Any(t => t.Id == TestTenantId))
        {
            db.Tenants.Add(new Tenant
            {
                Id = TestTenantId,
                Name = "Test Tenant",
                Slug = "test",
                ApiKeyHash = "test-tenant-api-key-hash",
                IsActive = true
            });
            db.SaveChanges();
        }

        return host;
    }

    /// <summary>
    /// Mints an HS256 bearer token carrying the <c>tenant_id</c> + <c>sub</c> claims
    /// the API derives identity from. Signed with <see cref="TestJwtSecret"/>.
    /// </summary>
    public string CreateToken(Guid? tenantId = null, Guid? userId = null)
    {
        var creds = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestJwtSecret)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: TestJwtIssuer,
            audience: TestJwtAudience,
            claims:
            [
                new Claim("sub", (userId ?? Guid.NewGuid()).ToString()),
                new Claim("tenant_id", (tenantId ?? TestTenantId).ToString()),
                new Claim("role", "authenticated"),
            ],
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>
    /// Creates an HttpClient authenticated as the given tenant (defaults to the
    /// seeded test tenant) via a Bearer token.
    /// </summary>
    public HttpClient CreateTenantClient(Guid? tenantId = null)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateToken(tenantId));
        return client;
    }

    /// <summary>
    /// A DI scope bound to <paramref name="tenantId"/> (defaults to the seeded test
    /// tenant), so its DbContext's tenant query filter shows that tenant's rows.
    /// </summary>
    public IServiceScope CreateTenantScope(Guid? tenantId = null)
    {
        var scope = Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().Set(tenantId ?? TestTenantId);
        return scope;
    }

    /// <summary>
    /// DbContext subclass that ignores the Pgvector Vector property
    /// since InMemory provider cannot map that type.
    /// </summary>
    private sealed class InMemoryOmniSiftDbContext : OmniSiftDbContext
    {
        public InMemoryOmniSiftDbContext(DbContextOptions<OmniSiftDbContext> options, ITenantContext tenantContext)
            : base(options, tenantContext) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<DocumentChunk>().Ignore(e => e.Embedding);
        }
    }
}
