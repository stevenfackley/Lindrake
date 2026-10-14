namespace OmniSift.Api.Services;

public interface IAuditLogger
{
    Task LogAsync(string action, string resourceType, Guid? resourceId = null, CancellationToken ct = default);
}
