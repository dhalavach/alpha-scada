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
