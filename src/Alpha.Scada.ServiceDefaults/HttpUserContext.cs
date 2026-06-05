/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.ServiceDefaults/HttpUserContext.cs
- Module role: Alpha.Scada.ServiceDefaults is shared platform infrastructure. It centralizes auth, service clients, resiliency, operational endpoints, database setup, and Wolverine/NATS conventions so services stay small.
- Local role: This file contributes one focused piece of the service; read it together with the adjacent Domain, Application, Infrastructure, and Program files.
- Architecture connection: ServiceDefaults is deliberately shared infrastructure; keep reusable platform concerns here, not domain-specific business logic.
- .NET/C# concepts to notice: File-scoped namespaces and small sealed classes/records are modern C# style choices that reduce boilerplate and make ownership explicit.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

using Microsoft.AspNetCore.Http;

namespace Alpha.Scada.ServiceDefaults;

public static class HttpUserContext
{
    public static void ForwardAuthorizationFrom(this HttpRequestMessage request, HttpRequest source)
    {
        request.ForwardAuthorization(source.Headers.Authorization.ToString());
    }

    public static void ForwardAuthorization(this HttpRequestMessage request, string authorizationHeader)
    {
        request.Headers.Remove("Authorization");
        if (!string.IsNullOrWhiteSpace(authorizationHeader))
        {
            request.Headers.TryAddWithoutValidation("Authorization", authorizationHeader);
        }
    }
}
