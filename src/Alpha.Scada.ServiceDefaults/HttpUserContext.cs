/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.ServiceDefaults/HttpUserContext.cs
- Module role: Alpha.Scada.ServiceDefaults is shared platform infrastructure. It centralizes auth, service clients, resiliency, operational endpoints, database setup, and Wolverine/NATS conventions so services stay small.
- Local role: This file contributes one focused piece of the service; read it together with the adjacent Domain, Application, Infrastructure, and Program files.
- Architecture connection: ServiceDefaults is deliberately shared infrastructure; keep reusable platform concerns here, not domain-specific business logic.
- .NET/C# concepts to notice: File-scoped namespaces and small sealed classes/records are modern C# style choices that reduce boilerplate and make ownership explicit.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Microsoft.AspNetCore.Http;

// LEARN: declares the logical namespace; namespaces organize types and help dependency direction stay visible.
namespace Alpha.Scada.ServiceDefaults;

// LEARN: declares a static helper class whose members are called on the type itself.
public static class HttpUserContext
{
// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    public static void ForwardAuthorizationFrom(this HttpRequestMessage request, HttpRequest source)
    {
// LEARN: executes one C# statement; semicolons terminate most statements.
        request.ForwardAuthorization(source.Headers.Authorization.ToString());
    }

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    public static void ForwardAuthorization(this HttpRequestMessage request, string authorizationHeader)
    {
// LEARN: executes one C# statement; semicolons terminate most statements.
        request.Headers.Remove("Authorization");
// LEARN: branches only when the boolean condition is true.
        if (!string.IsNullOrWhiteSpace(authorizationHeader))
        {
// LEARN: executes one C# statement; semicolons terminate most statements.
            request.Headers.TryAddWithoutValidation("Authorization", authorizationHeader);
        }
    }
}
