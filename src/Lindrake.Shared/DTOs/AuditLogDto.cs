namespace OmniSift.Shared.DTOs;

public sealed record AuditLogDto
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public Guid? UserId { get; init; }
    public string Action { get; init; } = string.Empty;
    public string ResourceType { get; init; } = string.Empty;
    public Guid? ResourceId { get; init; }
    public string? IpAddress { get; init; }
    public DateTime CreatedAt { get; init; }
}
