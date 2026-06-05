/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.ServiceDefaults/AlphaAuthentication.cs
- Module role: Alpha.Scada.ServiceDefaults is shared platform infrastructure. It centralizes auth, service clients, resiliency, operational endpoints, database setup, and Wolverine/NATS conventions so services stay small.
- Local role: This file mostly defines DTOs/messages. C# records are used here because cross-boundary payloads should be immutable, comparable value shapes.
- Architecture connection: ServiceDefaults is deliberately shared infrastructure; keep reusable platform concerns here, not domain-specific business logic.
- .NET/C# concepts to notice: Authentication/authorization are standard ASP.NET Core concepts: middleware validates tokens, then policies/claims decide access. C# records are concise immutable data carriers with value-based equality, useful for DTOs and domain events. async/await represents non-blocking I/O; it matters here because database, HTTP, NATS, and SignalR calls should not block server threads.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Alpha.Scada.Contracts;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Alpha.Scada.ServiceDefaults;

public sealed record AuthenticatedUser(CurrentUserDto Current)
{
    public static ValueTask<AuthenticatedUser> BindAsync(HttpContext context)
    {
        var principal = context.User;
        if (principal.Identity?.IsAuthenticated != true)
        {
            throw new InvalidOperationException("Authenticated user is required.");
        }

        return ValueTask.FromResult(new AuthenticatedUser(new CurrentUserDto(
            RequiredGuid(principal, JwtRegisteredClaimNames.Sub),
            RequiredGuid(principal, "tenant_id"),
            principal.FindFirstValue(JwtRegisteredClaimNames.Email) ?? string.Empty,
            principal.FindFirstValue("name") ?? string.Empty,
            principal.FindFirstValue("role") ?? Roles.Viewer)));
    }

    private static Guid RequiredGuid(ClaimsPrincipal principal, string claimType)
    {
        var value = principal.FindFirstValue(claimType);
        if (!Guid.TryParse(value, out var id))
        {
            throw new InvalidOperationException($"Authenticated token is missing required claim '{claimType}'.");
        }

        return id;
    }
}

public static class AlphaAuthentication
{
    public static IServiceCollection AddAlphaJwtAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<JwtBearerOptions>? configure = null)
    {
        services.AddJwtTokenService(configuration);
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = JwtTokenService.CreateValidationParameters(configuration);
                configure?.Invoke(options);
            });
        services.AddAuthorization();
        return services;
    }

    public static IApplicationBuilder UseAlphaAuthorization(this IApplicationBuilder app)
    {
        app.UseAuthentication();
        app.UseAuthorization();
        return app;
    }
}
