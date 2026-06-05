/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Gateway/Application/GatewayAuth.cs
- Module role: Alpha.Scada.Gateway is the public boundary/BFF. It keeps the React UI talking to one API surface, owns SignalR realtime fan-out, and translates browser-facing requests into calls or messages for backend services.
- Local role: This file sits in the application layer, where use cases coordinate domain concepts and infrastructure ports.
- Architecture connection: application files orchestrate use cases and message handling; they may call repositories/clients but should keep transport details at the boundary.
- .NET/C# concepts to notice: File-scoped namespaces and small sealed classes/records are modern C# style choices that reduce boilerplate and make ownership explicit.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

using Alpha.Scada.ServiceDefaults;

namespace Alpha.Scada.Gateway.Application;

public static class GatewayAuth
{
    public static HttpRequestMessage WithBearerToken(this HttpRequestMessage request, HttpContext context)
    {
        request.ForwardAuthorizationFrom(context.Request);
        return request;
    }
}
