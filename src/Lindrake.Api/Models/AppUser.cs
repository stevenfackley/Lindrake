namespace Lindrake.Api.Models;

public sealed class AppUser : ITenantOwned, ICreationTimestamped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Role { get; set; } = "member";
    public DateTime CreatedAt { get; set; }

    public Tenant Tenant { get; set; } = null!;
}
