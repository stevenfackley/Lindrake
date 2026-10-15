// ============================================================
// Integration Tests — Custom WebApplicationFactory
// Configures in-memory database and mocked services for testing
// ============================================================

using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Lindrake.Api.Data;
using Lindrake.Api.Middleware;
using Lindrake.Api.Models;
using Lindrake.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.SemanticKernel;
using Moq;
using Pgvector;

namespace Lindrake.IntegrationTests;

/// <summary>
/// Custom WebApplicationFactory that replaces PostgreSQL with an in-memory
/// database (unless <see cref="PostgresConnectionString"/> is set) and mocks
/// external services (embedding, LLM).
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
    // used to mint matching tokens. Must agree with the validator in AddLindrakeAuth.
    public const string TestJwtSecret = "lindrake-integration-tests-hs256-signing-secret-key";
    public const string TestJwtIssuer = "Lindrake";
    public const string TestJwtAudience = "Lindrake";

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

    /// <summary>
    /// Set by the Postgres suite (<see cref="Postgres.PostgresFixture"/>): the API keeps
    /// its real Npgsql registration (<see cref="TenantConnectionInterceptor"/> and
    /// <see cref="TimestampInterceptor"/> included) against this connection string
    /// instead of switching to InMemory, and nothing is seeded here (the fixture runs
    /// the migrations, whose seed includes <see cref="TestTenantId"/>). Null = InMemory.
    /// </summary>
    public string? PostgresConnectionString { get; init; }

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
                ["ConnectionStrings:" + LindrakeDatabase.DefaultConnectionName] = PostgresConnectionString,
            });
            config.AddInMemoryCollection(ExtraSettings);
        });

        builder.ConfigureServices(services =>
        {
            if (PostgresConnectionString is null)
                UseInMemoryDatabase(services);

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

    /// <summary>
    /// Replaces PostgreSQL with InMemory. EF Core allows one provider per context, and
    /// AddDbContext's UseNpgsql lives on in the context's IDbContextOptionsConfiguration,
    /// so remove exactly what AddDbContext registered (by type, not by name) and
    /// register the context again.
    /// </summary>
    private static void UseInMemoryDatabase(IServiceCollection services)
    {
        services.RemoveAll<LindrakeDbContext>();
        services.RemoveAll<DbContextOptions<LindrakeDbContext>>();
        services.RemoveAll<DbContextOptions>();
        services.RemoveAll<IDbContextOptionsConfiguration<LindrakeDbContext>>();

        // One database per factory (per class fixture, or per test for tests
        // that build their own factory); tests isolate further by tenant.
        var dbName = "Lindrake_IntegrationTests_" + Guid.NewGuid();
        services.AddDbContext<LindrakeDbContext>((sp, options) => options
            .UseInMemoryDatabase(dbName)
            .ReplaceService<IModelCustomizer, InMemoryModelCustomizer>()
            .AddInterceptors(sp.GetRequiredService<TimestampInterceptor>()));
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        if (PostgresConnectionString is not null)
            return host;

        // Seed the test database after the host is fully built so Serilog's
        // ReloadableLogger is not frozen prematurely by a premature BuildServiceProvider call.
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LindrakeDbContext>();
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
    public string CreateToken(Guid? tenantId = null, Guid? userId = null) => CreateSignedToken(
        [
            new Claim("sub", (userId ?? Guid.NewGuid()).ToString()),
            new Claim("tenant_id", (tenantId ?? TestTenantId).ToString()),
            new Claim("role", "authenticated"),
        ],
        expires: DateTime.UtcNow.AddHours(1));

    /// <summary>
    /// Mints an HS256 token carrying exactly <paramref name="claims"/>, valid for the
    /// hour before <paramref name="expires"/> (which may lie in the past, for an
    /// expired token). Signed with <see cref="TestJwtSecret"/>.
    /// </summary>
    public static string CreateSignedToken(IEnumerable<Claim> claims, DateTime expires)
    {
        var creds = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestJwtSecret)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: TestJwtIssuer,
            audience: TestJwtAudience,
            claims: claims,
            notBefore: expires.AddHours(-1),
            expires: expires,
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
    /// Builds the production model (OnModelCreating), then drops the Pgvector
    /// embedding column, whose type the InMemory provider cannot map.
    /// </summary>
    internal sealed class InMemoryModelCustomizer(ModelCustomizerDependencies dependencies)
        : ModelCustomizer(dependencies)
    {
        public override void Customize(ModelBuilder modelBuilder, DbContext context)
        {
            base.Customize(modelBuilder, context);
            modelBuilder.Entity<DocumentChunk>().Ignore(e => e.Embedding);
        }
    }
}
