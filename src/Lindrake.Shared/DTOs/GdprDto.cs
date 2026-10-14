namespace OmniSift.Shared.DTOs;

public sealed record TenantExportDto
{
    public Guid TenantId { get; init; }
    public List<DataSourceDto> DataSources { get; init; } = [];
    public List<QueryHistoryDto> QueryHistory { get; init; } = [];
    public List<AuditLogDto> AuditLog { get; init; } = [];
}

public sealed record QueryHistoryDto
{
    public Guid Id { get; init; }
    public string QueryText { get; init; } = string.Empty;
    public string? ResponseText { get; init; }
    public List<string> PluginsUsed { get; init; } = [];
    public int? DurationMs { get; init; }
    public DateTime CreatedAt { get; init; }
}

public sealed record DeleteAccountDataRequest
{
    public string Confirm { get; init; } = string.Empty;
}
