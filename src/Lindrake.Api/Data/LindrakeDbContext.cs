// ============================================================
// OmniSift.Api — Entity Framework Core DbContext
// Configured for PostgreSQL + pgvector with multi-tenancy
// ============================================================

using System.Linq.Expressions;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using OmniSift.Api.Middleware;
using OmniSift.Api.Models;
using OmniSift.Shared;
using OmniSift.Shared.DTOs;

namespace OmniSift.Api.Data;

/// <summary>
/// Primary database context for OmniSift.
/// Configures entity mappings for PostgreSQL with pgvector support.
/// Tenant isolation has two layers: a global query filter on every
/// <see cref="ITenantOwned"/> entity (scoped to the injected
/// <see cref="ITenantContext"/>), and PostgreSQL RLS policies keyed on the
/// <c>app.current_tenant</c> session variable.
/// </summary>
public class OmniSiftDbContext : DbContext
{
    /// <summary>
    /// Name of the tenant query filter, for targeted
    /// <c>IgnoreQueryFilters([TenantFilter])</c> at call sites that must see
    /// every tenant's rows (each one says why).
    /// </summary>
    public const string TenantFilter = "Tenant";

    public OmniSiftDbContext(DbContextOptions<OmniSiftDbContext> options, ITenantContext tenantContext)
        : base(options)
    {
        ArgumentNullException.ThrowIfNull(tenantContext);
        TenantContext = tenantContext;
    }

    /// <summary>The tenant this context's queries are scoped to.</summary>
    internal ITenantContext TenantContext { get; }

    /// <summary>
    /// Read by the tenant query filter on every query. <see cref="Guid.Empty"/> is
    /// never a valid tenant (<see cref="ITenantContext.Set"/> rejects it), so an
    /// unbound context matches no rows: it fails closed rather than throwing.
    /// </summary>
    private Guid CurrentTenantIdOrEmpty => TenantContext.CurrentTenantId ?? Guid.Empty;

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<DataSource> DataSources => Set<DataSource>();
    public DbSet<DocumentChunk> DocumentChunks => Set<DocumentChunk>();
    public DbSet<QueryHistory> QueryHistories => Set<QueryHistory>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<AppUser> Users => Set<AppUser>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Enable extensions
        modelBuilder.HasPostgresExtension("pg_trgm");  // trigram keyword search arm
        modelBuilder.HasPostgresExtension("uuid-ossp");
        modelBuilder.HasPostgresExtension("vector");

        // ── Tenant ──────────────────────────────────────
        modelBuilder.Entity<Tenant>(entity =>
        {
            entity.ToTable("tenants");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name").HasMaxLength(256).IsRequired();
            entity.Property(e => e.Slug).HasColumnName("slug").HasMaxLength(128).IsRequired();
            entity.Property(e => e.IsActive).HasColumnName("is_active").HasDefaultValue(true);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");

            entity.HasIndex(e => e.Slug).IsUnique();

            entity.Property(e => e.ApiKeyHash).HasColumnName("api_key_hash").HasMaxLength(64).IsRequired();
            entity.HasIndex(e => e.ApiKeyHash).IsUnique();
        });

        // ── DataSource ──────────────────────────────────
        modelBuilder.Entity<DataSource>(entity =>
        {
            entity.ToTable("data_sources");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.TenantId).HasColumnName("tenant_id").IsRequired();
            entity.Property(e => e.SourceType).HasColumnName("source_type").HasMaxLength(50).IsRequired();
            entity.Property(e => e.FileName).HasColumnName("file_name").HasMaxLength(512);
            entity.Property(e => e.OriginalUrl).HasColumnName("original_url").HasMaxLength(2048);

            // IngestionStatus stored as lowercase string for DB compatibility
            entity.Property(e => e.Status)
                .HasColumnName("status")
                .HasMaxLength(50)
                .HasConversion(
                    v => v.ToWireString(),
                    v => IngestionStatusExtensions.ParseWireString(v))
                .HasDefaultValue(IngestionStatus.Pending);

            entity.Property(e => e.ErrorMessage).HasColumnName("error_message");
            entity.Property(e => e.Metadata).HasColumnName("metadata").HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb")
                .HasConversion(JsonDictionaryConverter);
            entity.Property(e => e.HasPii).HasColumnName("has_pii").HasDefaultValue(false);
            entity.Property(e => e.PiiFlags).HasColumnName("pii_flags").HasColumnType("jsonb")
                .HasConversion(JsonStringListConverter);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");

            entity.HasOne(e => e.Tenant)
                  .WithMany(t => t.DataSources)
                  .HasForeignKey(e => e.TenantId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.TenantId);
            entity.HasIndex(e => new { e.TenantId, e.Status });
        });

        // ── DocumentChunk ───────────────────────────────
        modelBuilder.Entity<DocumentChunk>(entity =>
        {
            entity.ToTable("document_chunks");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.TenantId).HasColumnName("tenant_id").IsRequired();
            entity.Property(e => e.DataSourceId).HasColumnName("data_source_id").IsRequired();
            entity.Property(e => e.Content).HasColumnName("content").IsRequired();
            entity.Property(e => e.ChunkIndex).HasColumnName("chunk_index").IsRequired();
            entity.Property(e => e.TokenCount).HasColumnName("token_count").HasDefaultValue(0);
            entity.Property(e => e.Embedding).HasColumnName("embedding").HasColumnType("vector(384)");
            entity.Property(e => e.Metadata).HasColumnName("metadata").HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb")
                .HasConversion(JsonDictionaryConverter);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");

            entity.HasOne(e => e.Tenant)
                  .WithMany(t => t.DocumentChunks)
                  .HasForeignKey(e => e.TenantId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.DataSource)
                  .WithMany(ds => ds.Chunks)
                  .HasForeignKey(e => e.DataSourceId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.TenantId);
            entity.HasIndex(e => e.DataSourceId);
        });

        // ── QueryHistory ────────────────────────────────
        modelBuilder.Entity<QueryHistory>(entity =>
        {
            entity.ToTable("query_history");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.TenantId).HasColumnName("tenant_id").IsRequired();
            entity.Property(e => e.QueryText).HasColumnName("query_text").IsRequired();
            entity.Property(e => e.ResponseText).HasColumnName("response_text");
            entity.Property(e => e.PluginsUsed).HasColumnName("plugins_used").HasColumnType("jsonb").HasDefaultValueSql("'[]'::jsonb")
                .HasConversion(
                    v => JsonSerializer.Serialize(v, JsonSerializerOptions.Default),
                    v => JsonSerializer.Deserialize<List<string>>(v, JsonSerializerOptions.Default) ?? new List<string>());
            entity.Property(e => e.Sources).HasColumnName("sources").HasColumnType("jsonb").HasDefaultValueSql("'[]'::jsonb")
                .HasConversion(
                    v => JsonSerializer.Serialize(v, OmniSiftJsonContext.Default.ListSourceCitation),
                    v => JsonSerializer.Deserialize(v, OmniSiftJsonContext.Default.ListSourceCitation) ?? new List<SourceCitation>());
            entity.Property(e => e.DurationMs).HasColumnName("duration_ms");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");

            entity.HasOne(e => e.Tenant)
                  .WithMany(t => t.QueryHistories)
                  .HasForeignKey(e => e.TenantId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.TenantId);
            entity.HasIndex(e => new { e.TenantId, e.CreatedAt });
        });

        // ── AuditLog ────────────────────────────────────
        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.ToTable("audit_log");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.TenantId).HasColumnName("tenant_id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.Action).HasColumnName("action").HasMaxLength(128).IsRequired();
            entity.Property(e => e.ResourceType).HasColumnName("resource_type").HasMaxLength(128).IsRequired();
            entity.Property(e => e.ResourceId).HasColumnName("resource_id");
            entity.Property(e => e.IpAddress).HasColumnName("ip_address").HasMaxLength(64);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");

            entity.HasOne(e => e.Tenant)
                  .WithMany(t => t.AuditLogs)
                  .HasForeignKey(e => e.TenantId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.TenantId);
            entity.HasIndex(e => new { e.TenantId, e.CreatedAt });
        });

        // ── AppUser ─────────────────────────────────────
        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.TenantId).HasColumnName("tenant_id");
            entity.Property(e => e.Email).HasColumnName("email").HasMaxLength(256).IsRequired();
            entity.Property(e => e.PasswordHash).HasColumnName("password_hash").IsRequired();
            entity.Property(e => e.Role).HasColumnName("role").HasMaxLength(64).HasDefaultValue("member");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");

            entity.HasIndex(e => e.Email).IsUnique();
            entity.HasIndex(e => e.TenantId);

            entity.HasOne(e => e.Tenant)
                  .WithMany(t => t.Users)
                  .HasForeignKey(e => e.TenantId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        ApplyTenantFilters(modelBuilder);
    }

    /// <summary>
    /// Adds the named <see cref="TenantFilter"/> (<c>e.TenantId == current tenant</c>)
    /// to every entity implementing <see cref="ITenantOwned"/>. The model is built
    /// once and cached; the filter references this context instance, which EF
    /// re-binds to the executing context on every query.
    /// </summary>
    private void ApplyTenantFilters(ModelBuilder modelBuilder)
    {
        Expression<Func<Guid>> currentTenant = () => CurrentTenantIdOrEmpty;

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(ITenantOwned).IsAssignableFrom(entityType.ClrType))
                continue;

            var entity = Expression.Parameter(entityType.ClrType, "e");
            var filter = Expression.Lambda(
                Expression.Equal(
                    Expression.Property(entity, nameof(ITenantOwned.TenantId)),
                    currentTenant.Body),
                entity);

            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(TenantFilter, filter);
        }
    }

    /// <summary>
    /// Shared value converter for Dictionary&lt;string, object&gt; JSONB columns.
    /// Ensures compatibility with both Npgsql and InMemory providers.
    /// </summary>
    private static readonly ValueConverter<Dictionary<string, object>, string> JsonDictionaryConverter = new(
        v => JsonSerializer.Serialize(v, JsonSerializerOptions.Default),
        v => JsonSerializer.Deserialize<Dictionary<string, object>>(v, JsonSerializerOptions.Default) ?? new Dictionary<string, object>());

    /// <summary>
    /// Value converter for nullable List&lt;string&gt; JSONB columns (PII flags).
    /// </summary>
    private static readonly ValueConverter<List<string>?, string> JsonStringListConverter = new(
        v => v == null ? "[]" : JsonSerializer.Serialize(v, JsonSerializerOptions.Default),
        v => JsonSerializer.Deserialize<List<string>>(v, JsonSerializerOptions.Default));
}
