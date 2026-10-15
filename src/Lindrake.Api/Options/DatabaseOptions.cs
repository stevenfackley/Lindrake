// ============================================================
// Lindrake.Api — Database configuration options
// ============================================================

using System.ComponentModel.DataAnnotations;

namespace Lindrake.Api.Options;

/// <summary>
/// Where Lindrake's tables live and how many connections the API may hold.
/// Bound from the "Database" configuration section. The connection strings
/// themselves are <c>ConnectionStrings:DefaultConnection</c> (runtime) and the
/// optional <c>ConnectionStrings:Migrations</c> (startup migrations only).
/// </summary>
public sealed class DatabaseOptions
{
    public const string Section = "Database";

    /// <summary>
    /// The Postgres schema the tables are in: <c>public</c> for the local compose
    /// <c>db</c>, <c>lindrake</c> on qavren-db. The migrations name no schema, so
    /// the connecting role's <c>search_path</c> decides where tables land (qavren-db
    /// provisioning sets it to <c>lindrake, extensions</c>); this setting places the
    /// <c>__EFMigrationsHistory</c> table, and startup migrations refuse to run when
    /// the connection's <c>current_schema()</c> disagrees with it.
    /// </summary>
    [Required]
    [RegularExpression("^[a-z_][a-z0-9_]{0,62}$",
        ErrorMessage = "Database:Schema must be a lowercase, unquoted Postgres identifier.")]
    public string Schema { get; set; } = "public";

    /// <summary>
    /// Npgsql's <c>Maximum Pool Size</c> for every connection string the API uses
    /// (it replaces any value in the connection string). qavren-db is reached through
    /// the Supavisor session pooler, where each pooled connection holds a server
    /// connection for its lifetime, so keep it small. Default 10.
    /// </summary>
    [Range(1, 100)]
    public int MaxPoolSize { get; set; } = 10;
}
