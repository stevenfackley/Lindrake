// ============================================================
// Lindrake.Api — Audit log vocabulary
// ============================================================

namespace Lindrake.Api.Services;

/// <summary>
/// The <c>audit_log.action</c> values <see cref="IAuditLogger"/> is called with.
/// Stored rows and tenant exports carry these strings, so a value never changes
/// once released (lower-case snake_case, compared ordinally).
/// </summary>
public static class AuditActions
{
    /// <summary>POST /api/datasources/upload accepted a file.</summary>
    public const string Upload = "upload";

    /// <summary>POST /api/datasources/web stored a fetched page.</summary>
    public const string IngestWeb = "ingest_web";

    /// <summary>DELETE /api/datasources/{id} removed a data source.</summary>
    public const string Delete = "delete";

    /// <summary>A buffered research agent run finished (POST /api/agent/query).</summary>
    public const string AgentQuery = "agent_query";

    /// <summary>A streamed research agent run finished (POST /api/agent/query/stream).</summary>
    public const string AgentQueryStream = "agent_query_stream";

    /// <summary>DELETE /api/account/data erased the tenant's data.</summary>
    public const string DeleteAccountData = "delete_account_data";
}

/// <summary>The <c>audit_log.resource_type</c> values, as stable as <see cref="AuditActions"/>.</summary>
public static class AuditResources
{
    /// <summary>A <c>data_sources</c> row; the resource id is its id.</summary>
    public const string DataSource = "data_source";

    /// <summary>A <c>query_history</c> row; the resource id is its id.</summary>
    public const string QueryHistory = "query_history";

    /// <summary>The tenant's account; the resource id is the tenant id.</summary>
    public const string Account = "account";
}
