using System.Text.RegularExpressions;
using Lindrake.Api.Data;
using Lindrake.Api.Middleware;
using Lindrake.Api.Models;
using Lindrake.Api.Services;
using Lindrake.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Lindrake.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/[controller]")]
public sealed class AuthController(
    LindrakeDbContext dbContext,
    ITenantContext tenantContext,
    IPasswordHasher passwordHasher,
    JwtTokenService jwtTokenService) : ControllerBase
{
    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(
        [FromBody] RegisterRequest request,
        CancellationToken cancellationToken)
    {
        // Emails are unique across ALL tenants (unique index) and no tenant exists
        // yet, so this check must see every tenant's users: bypass the tenant filter.
        var emailExists = await dbContext.Users
            .IgnoreQueryFilters([LindrakeDbContext.TenantFilter])
            .AnyAsync(u => u.Email == request.Email, cancellationToken)
            .ConfigureAwait(false);
        if (emailExists)
            return Problem(
                detail: "An account with this email address already exists.",
                statusCode: StatusCodes.Status409Conflict);

        var slug = Slugify(request.TenantName) + "-" + Guid.NewGuid().ToString("N")[..8];

        var tenant = new Tenant
        {
            Name = request.TenantName,
            Slug = slug,
            ApiKeyHash = Guid.NewGuid().ToString("N")
        };

        dbContext.Tenants.Add(tenant);

        var user = new AppUser
        {
            TenantId = tenant.Id,
            Email = request.Email,
            PasswordHash = passwordHasher.Hash(request.Password),
            Role = "owner"
        };

        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var (token, expiresAt) = jwtTokenService.CreateToken(user);

        return Ok(new AuthResponse
        {
            Token = token,
            UserId = user.Id,
            TenantId = tenant.Id,
            Role = user.Role,
            ExpiresAt = expiresAt
        });
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        // The request names its tenant: bind the scope to it so the tenant query
        // filter scopes the lookup (Guid.Empty is never a tenant: no such user).
        if (request.TenantId == Guid.Empty)
            return Problem(detail: "Invalid credentials.", statusCode: StatusCodes.Status401Unauthorized);

        tenantContext.Set(request.TenantId);

        var user = await dbContext.Users
            .FirstOrDefaultAsync(u => u.Email == request.Email, cancellationToken)
            .ConfigureAwait(false);

        if (user is null || !passwordHasher.Verify(request.Password, user.PasswordHash))
            return Problem(detail: "Invalid credentials.", statusCode: StatusCodes.Status401Unauthorized);

        var (token, expiresAt) = jwtTokenService.CreateToken(user);

        return Ok(new AuthResponse
        {
            Token = token,
            UserId = user.Id,
            TenantId = user.TenantId,
            Role = user.Role,
            ExpiresAt = expiresAt
        });
    }

    private static string Slugify(string name) =>
        Regex.Replace(name.ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
}
