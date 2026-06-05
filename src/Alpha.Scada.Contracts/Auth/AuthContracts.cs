/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Contracts/Auth/AuthContracts.cs
- Module role: Alpha.Scada.Contracts is the shared DTO contract package. These types describe REST payloads and edge wire formats that cross service or process boundaries.
- Local role: This file mostly defines DTOs/messages. C# records are used here because cross-boundary payloads should be immutable, comparable value shapes.
- Architecture connection: this file participates in the service boundary. Follow the dependency direction from HTTP/message edges inward to application/domain code and outward to infrastructure.
- .NET/C# concepts to notice: Authentication/authorization are standard ASP.NET Core concepts: middleware validates tokens, then policies/claims decide access. C# records are concise immutable data carriers with value-based equality, useful for DTOs and domain events.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

namespace Alpha.Scada.Contracts;

// LEARN: declares an immutable C# record, commonly used for DTOs and message contracts.
public sealed record LoginRequest(string Email, string Password);

// LEARN: declares an immutable C# record, commonly used for DTOs and message contracts.
public sealed record LoginResponse(string AccessToken, DateTimeOffset ExpiresAtUtc, UserDto User);

// LEARN: declares an immutable C# record, commonly used for DTOs and message contracts.
public sealed record UserDto(Guid Id, Guid TenantId, string Email, string DisplayName, string Role);

// LEARN: declares an immutable C# record, commonly used for DTOs and message contracts.
public sealed record CurrentUserDto(Guid UserId, Guid TenantId, string Email, string DisplayName, string Role);

// LEARN: declares a static helper class whose members are called on the type itself.
public static class Roles
{
    public const string Admin = "Admin";
    public const string Operator = "Operator";
    public const string Viewer = "Viewer";
    public const string SupportEngineer = "SupportEngineer";
}

// LEARN: declares a static helper class whose members are called on the type itself.
public static class RoleRules
{
// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
    public static bool CanAcknowledge(string role) => role is Roles.Admin or Roles.Operator or Roles.SupportEngineer;

// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
    public static bool CanManageConfiguration(string role) => role is Roles.Admin or Roles.SupportEngineer;

// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
    public static bool IsSupport(string role) => role == Roles.SupportEngineer;
}
