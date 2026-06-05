/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Identity/Application/AuthService.cs
- Module role: Alpha.Scada.Identity is the identity service. It owns local users, password hashing, role assignment, and JWT issuance, so other services can trust bearer-token claims instead of duplicating credential logic.
- Local role: This file is an application-service layer: it coordinates repositories, policies, external calls, and DTOs without being an HTTP controller itself.
- Architecture connection: application files orchestrate use cases and message handling; they may call repositories/clients but should keep transport details at the boundary.
- .NET/C# concepts to notice: Authentication/authorization are standard ASP.NET Core concepts: middleware validates tokens, then policies/claims decide access. async/await represents non-blocking I/O; it matters here because database, HTTP, NATS, and SignalR calls should not block server threads.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Contracts;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Identity.Infrastructure;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.ServiceDefaults;

// LEARN: declares the logical namespace; namespaces organize types and help dependency direction stay visible.
namespace Alpha.Scada.Identity.Application;

// LEARN: declares a class; sealed means no other class can inherit from it.
public sealed class AuthService(IdentityRepository repository, JwtTokenService tokens)
{
// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task<LoginResponse?> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var user = await repository.GetByEmailAsync(request.Email, cancellationToken);
// LEARN: branches only when the boolean condition is true.
        if (user is null || !PasswordHasher.Verify(request.Password, user.PasswordHash))
        {
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await repository.WriteAuditAsync(null, null, "auth.login_failed", $"Login failed for {request.Email}", cancellationToken);
// LEARN: returns a value or exits the current method.
            return null;
        }

// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await repository.WriteAuditAsync(user.TenantId, user.Id, "auth.login", "User logged in", cancellationToken);
// LEARN: returns a value or exits the current method.
        return tokens.Issue(IdentityRepository.ToDto(user), TimeSpan.FromHours(12));
    }
}
