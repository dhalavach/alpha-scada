/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.ServiceDefaults/AlphaAuthentication.cs
- Module role: Alpha.Scada.ServiceDefaults is shared platform infrastructure. It centralizes auth, service clients, resiliency, operational endpoints, database setup, and Wolverine/NATS conventions so services stay small.
- Local role: This file mostly defines DTOs/messages. C# records are used here because cross-boundary payloads should be immutable, comparable value shapes.
- Architecture connection: ServiceDefaults is deliberately shared infrastructure; keep reusable platform concerns here, not domain-specific business logic.
- .NET/C# concepts to notice: Authentication/authorization are standard ASP.NET Core concepts: middleware validates tokens, then policies/claims decide access. C# records are concise immutable data carriers with value-based equality, useful for DTOs and domain events. async/await represents non-blocking I/O; it matters here because database, HTTP, NATS, and SignalR calls should not block server threads.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using System.IdentityModel.Tokens.Jwt;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using System.Security.Claims;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Contracts;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Microsoft.AspNetCore.Authentication.JwtBearer;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Microsoft.AspNetCore.Builder;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Microsoft.AspNetCore.Http;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Microsoft.Extensions.Configuration;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Microsoft.Extensions.DependencyInjection;

// LEARN: declares the logical namespace; namespaces organize types and help dependency direction stay visible.
namespace Alpha.Scada.ServiceDefaults;

// LEARN: declares an immutable C# record, commonly used for DTOs and message contracts.
public sealed record AuthenticatedUser(CurrentUserDto Current)
{
// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    public static ValueTask<AuthenticatedUser> BindAsync(HttpContext context)
    {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var principal = context.User;
// LEARN: branches only when the boolean condition is true.
        if (principal.Identity?.IsAuthenticated != true)
        {
// LEARN: throws an exception to signal that this path cannot continue safely.
            throw new InvalidOperationException("Authenticated user is required.");
        }

// LEARN: returns a value or exits the current method.
        return ValueTask.FromResult(new AuthenticatedUser(new CurrentUserDto(
// LEARN: continues an argument/object/collection initializer onto the next line.
            RequiredGuid(principal, JwtRegisteredClaimNames.Sub),
// LEARN: continues an argument/object/collection initializer onto the next line.
            RequiredGuid(principal, "tenant_id"),
// LEARN: continues an argument/object/collection initializer onto the next line.
            principal.FindFirstValue(JwtRegisteredClaimNames.Email) ?? string.Empty,
// LEARN: continues an argument/object/collection initializer onto the next line.
            principal.FindFirstValue("name") ?? string.Empty,
// LEARN: executes one C# statement; semicolons terminate most statements.
            principal.FindFirstValue("role") ?? Roles.Viewer)));
    }

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private static Guid RequiredGuid(ClaimsPrincipal principal, string claimType)
    {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var value = principal.FindFirstValue(claimType);
// LEARN: branches only when the boolean condition is true.
        if (!Guid.TryParse(value, out var id))
        {
// LEARN: throws an exception to signal that this path cannot continue safely.
            throw new InvalidOperationException($"Authenticated token is missing required claim '{claimType}'.");
        }

// LEARN: returns a value or exits the current method.
        return id;
    }
}

// LEARN: declares a static helper class whose members are called on the type itself.
public static class AlphaAuthentication
{
// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    public static IServiceCollection AddAlphaJwtAuthentication(
// LEARN: continues an argument/object/collection initializer onto the next line.
        this IServiceCollection services,
// LEARN: continues an argument/object/collection initializer onto the next line.
        IConfiguration configuration,
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
        Action<JwtBearerOptions>? configure = null)
    {
// LEARN: executes one C# statement; semicolons terminate most statements.
        services.AddJwtTokenService(configuration);
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
            .AddJwtBearer(options =>
            {
// LEARN: executes one C# statement; semicolons terminate most statements.
                options.MapInboundClaims = false;
// LEARN: executes one C# statement; semicolons terminate most statements.
                options.TokenValidationParameters = JwtTokenService.CreateValidationParameters(configuration);
// LEARN: executes one C# statement; semicolons terminate most statements.
                configure?.Invoke(options);
// LEARN: executes one C# statement; semicolons terminate most statements.
            });
// LEARN: executes one C# statement; semicolons terminate most statements.
        services.AddAuthorization();
// LEARN: returns a value or exits the current method.
        return services;
    }

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    public static IApplicationBuilder UseAlphaAuthorization(this IApplicationBuilder app)
    {
// LEARN: executes one C# statement; semicolons terminate most statements.
        app.UseAuthentication();
// LEARN: executes one C# statement; semicolons terminate most statements.
        app.UseAuthorization();
// LEARN: returns a value or exits the current method.
        return app;
    }
}
