using System.ComponentModel.DataAnnotations;

namespace OmniSift.Shared.DTOs;

public sealed record RegisterRequest
{
    [Required]
    [EmailAddress]
    public string Email { get; init; } = string.Empty;

    [Required]
    [MinLength(8)]
    public string Password { get; init; } = string.Empty;

    [Required]
    public string TenantName { get; init; } = string.Empty;
}

public sealed record LoginRequest
{
    [Required]
    [EmailAddress]
    public string Email { get; init; } = string.Empty;

    [Required]
    public string Password { get; init; } = string.Empty;

    [Required]
    public Guid TenantId { get; init; }
}

public sealed record AuthResponse
{
    public string Token { get; init; } = string.Empty;
    public Guid UserId { get; init; }
    public Guid TenantId { get; init; }
    public string Role { get; init; } = string.Empty;
    public DateTime ExpiresAt { get; init; }
}
