/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.ServiceDefaults/JwtTokenService.cs
- Module role: Alpha.Scada.ServiceDefaults is shared platform infrastructure. It centralizes auth, service clients, resiliency, operational endpoints, database setup, and Wolverine/NATS conventions so services stay small.
- Local role: This file is an application-service layer: it coordinates repositories, policies, external calls, and DTOs without being an HTTP controller itself.
- Architecture connection: ServiceDefaults is deliberately shared infrastructure; keep reusable platform concerns here, not domain-specific business logic.
- .NET/C# concepts to notice: Authentication/authorization are standard ASP.NET Core concepts: middleware validates tokens, then policies/claims decide access.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Alpha.Scada.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Alpha.Scada.ServiceDefaults;

public sealed class JwtTokenService(IConfiguration configuration)
{
    private readonly JwtSecurityTokenHandler _handler = new() { MapInboundClaims = false };
    private readonly SymmetricSecurityKey _signingKey = new(GetSigningSecret(configuration));
    private readonly TokenValidationParameters _validationParameters = CreateValidationParameters(configuration);

    public static byte[] GetSigningSecret(IConfiguration configuration)
    {
        var secret = configuration["Jwt:Secret"];
        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new InvalidOperationException("Jwt:Secret must be configured. Set JWT_SECRET in .env or Jwt__Secret in the environment.");
        }

        return Encoding.UTF8.GetBytes(secret);
    }

    public static TokenValidationParameters CreateValidationParameters(IConfiguration configuration)
    {
        return new TokenValidationParameters
        {
            IssuerSigningKey = new SymmetricSecurityKey(GetSigningSecret(configuration)),
            ValidateIssuerSigningKey = true,
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "name",
            RoleClaimType = "role"
        };
    }

    public LoginResponse Issue(UserDto user, TimeSpan lifetime)
    {
        var expires = DateTimeOffset.UtcNow.Add(lifetime);
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity([
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim("tenant_id", user.TenantId.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                new Claim("name", user.DisplayName),
                new Claim("role", user.Role)
            ]),
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256)
        };
        return new LoginResponse(_handler.WriteToken(_handler.CreateToken(descriptor)), expires, user);
    }

    public CurrentUserDto? Validate(string token)
    {
        try
        {
            var principal = _handler.ValidateToken(token, _validationParameters, out _);
            var userId = Guid.Parse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? "");
            var tenantId = Guid.Parse(principal.FindFirstValue("tenant_id") ?? "");
            return new CurrentUserDto(
                userId,
                tenantId,
                principal.FindFirstValue(JwtRegisteredClaimNames.Email) ?? "",
                principal.FindFirstValue("name") ?? "",
                principal.FindFirstValue("role") ?? Roles.Viewer);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or SecurityTokenException)
        {
            return null;
        }
    }
}

public static class JwtTokenServiceRegistration
{
    public static IServiceCollection AddJwtTokenService(this IServiceCollection services, IConfiguration configuration)
    {
        _ = JwtTokenService.GetSigningSecret(configuration);
        services.AddSingleton<JwtTokenService>();
        return services;
    }
}
