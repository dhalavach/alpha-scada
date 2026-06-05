/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.ServiceDefaults/JwtTokenService.cs
- Module role: Alpha.Scada.ServiceDefaults is shared platform infrastructure. It centralizes auth, service clients, resiliency, operational endpoints, database setup, and Wolverine/NATS conventions so services stay small.
- Local role: This file is an application-service layer: it coordinates repositories, policies, external calls, and DTOs without being an HTTP controller itself.
- Architecture connection: ServiceDefaults is deliberately shared infrastructure; keep reusable platform concerns here, not domain-specific business logic.
- .NET/C# concepts to notice: Authentication/authorization are standard ASP.NET Core concepts: middleware validates tokens, then policies/claims decide access.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using System.IdentityModel.Tokens.Jwt;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using System.Security.Claims;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using System.Text;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Contracts;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Microsoft.Extensions.Configuration;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Microsoft.Extensions.DependencyInjection;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Microsoft.IdentityModel.Tokens;

// LEARN: declares the logical namespace; namespaces organize types and help dependency direction stay visible.
namespace Alpha.Scada.ServiceDefaults;

// LEARN: declares a class; sealed means no other class can inherit from it.
public sealed class JwtTokenService(IConfiguration configuration)
{
// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private readonly JwtSecurityTokenHandler _handler = new() { MapInboundClaims = false };
// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private readonly SymmetricSecurityKey _signingKey = new(GetSigningSecret(configuration));
// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private readonly TokenValidationParameters _validationParameters = CreateValidationParameters(configuration);

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    public static byte[] GetSigningSecret(IConfiguration configuration)
    {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var secret = configuration["Jwt:Secret"];
// LEARN: branches only when the boolean condition is true.
        if (string.IsNullOrWhiteSpace(secret))
        {
// LEARN: throws an exception to signal that this path cannot continue safely.
            throw new InvalidOperationException("Jwt:Secret must be configured. Set JWT_SECRET in .env or Jwt__Secret in the environment.");
        }

// LEARN: returns a value or exits the current method.
        return Encoding.UTF8.GetBytes(secret);
    }

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    public static TokenValidationParameters CreateValidationParameters(IConfiguration configuration)
    {
// LEARN: returns a value or exits the current method.
        return new TokenValidationParameters
        {
// LEARN: creates a new object or record instance.
            IssuerSigningKey = new SymmetricSecurityKey(GetSigningSecret(configuration)),
// LEARN: continues an argument/object/collection initializer onto the next line.
            ValidateIssuerSigningKey = true,
// LEARN: continues an argument/object/collection initializer onto the next line.
            ValidateIssuer = false,
// LEARN: continues an argument/object/collection initializer onto the next line.
            ValidateAudience = false,
// LEARN: continues an argument/object/collection initializer onto the next line.
            ValidateLifetime = true,
// LEARN: continues an argument/object/collection initializer onto the next line.
            ClockSkew = TimeSpan.FromSeconds(30),
// LEARN: continues an argument/object/collection initializer onto the next line.
            NameClaimType = "name",
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
            RoleClaimType = "role"
        };
    }

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    public LoginResponse Issue(UserDto user, TimeSpan lifetime)
    {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var expires = DateTimeOffset.UtcNow.Add(lifetime);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var descriptor = new SecurityTokenDescriptor
        {
// LEARN: creates a new object or record instance.
            Subject = new ClaimsIdentity([
// LEARN: creates a new object or record instance.
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
// LEARN: creates a new object or record instance.
                new Claim("tenant_id", user.TenantId.ToString()),
// LEARN: creates a new object or record instance.
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
// LEARN: creates a new object or record instance.
                new Claim("name", user.DisplayName),
// LEARN: creates a new object or record instance.
                new Claim("role", user.Role)
// LEARN: continues an argument/object/collection initializer onto the next line.
            ]),
// LEARN: continues an argument/object/collection initializer onto the next line.
            Expires = expires.UtcDateTime,
// LEARN: creates a new object or record instance.
            SigningCredentials = new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256)
        };
// LEARN: returns a value or exits the current method.
        return new LoginResponse(_handler.WriteToken(_handler.CreateToken(descriptor)), expires, user);
    }

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    public CurrentUserDto? Validate(string token)
    {
// LEARN: starts a protected block whose exceptions can be handled below.
        try
        {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var principal = _handler.ValidateToken(token, _validationParameters, out _);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var userId = Guid.Parse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? "");
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var tenantId = Guid.Parse(principal.FindFirstValue("tenant_id") ?? "");
// LEARN: returns a value or exits the current method.
            return new CurrentUserDto(
// LEARN: continues an argument/object/collection initializer onto the next line.
                userId,
// LEARN: continues an argument/object/collection initializer onto the next line.
                tenantId,
// LEARN: continues an argument/object/collection initializer onto the next line.
                principal.FindFirstValue(JwtRegisteredClaimNames.Email) ?? "",
// LEARN: continues an argument/object/collection initializer onto the next line.
                principal.FindFirstValue("name") ?? "",
// LEARN: executes one C# statement; semicolons terminate most statements.
                principal.FindFirstValue("role") ?? Roles.Viewer);
        }
// LEARN: handles a specific exception path; filters with when further narrow when it applies.
        catch (Exception ex) when (ex is ArgumentException or FormatException or SecurityTokenException)
        {
// LEARN: returns a value or exits the current method.
            return null;
        }
    }
}

// LEARN: declares a static helper class whose members are called on the type itself.
public static class JwtTokenServiceRegistration
{
// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    public static IServiceCollection AddJwtTokenService(this IServiceCollection services, IConfiguration configuration)
    {
// LEARN: executes one C# statement; semicolons terminate most statements.
        _ = JwtTokenService.GetSigningSecret(configuration);
// LEARN: executes one C# statement; semicolons terminate most statements.
        services.AddSingleton<JwtTokenService>();
// LEARN: returns a value or exits the current method.
        return services;
    }
}
