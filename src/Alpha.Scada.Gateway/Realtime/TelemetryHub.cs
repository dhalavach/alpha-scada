/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Gateway/Realtime/TelemetryHub.cs
- Module role: Alpha.Scada.Gateway is the public boundary/BFF. It keeps the React UI talking to one API surface, owns SignalR realtime fan-out, and translates browser-facing requests into calls or messages for backend services.
- Local role: This file contributes one focused piece of the service; read it together with the adjacent Domain, Application, Infrastructure, and Program files.
- Architecture connection: this file participates in the service boundary. Follow the dependency direction from HTTP/message edges inward to application/domain code and outward to infrastructure.
- .NET/C# concepts to notice: SignalR provides server-to-browser realtime delivery; Gateway owns that public realtime boundary. async/await represents non-blocking I/O; it matters here because database, HTTP, NATS, and SignalR calls should not block server threads.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Microsoft.AspNetCore.Authorization;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Microsoft.AspNetCore.SignalR;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using System.Security.Claims;

// LEARN: declares the logical namespace; namespaces organize types and help dependency direction stay visible.
namespace Alpha.Scada.Gateway.Realtime;

// LEARN: continues the current C# construct; indentation shows the surrounding scope.
[Authorize]
// LEARN: declares a class; sealed means no other class can inherit from it.
public sealed class TelemetryHub : Hub
{
// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
    public static string TenantGroup(Guid tenantId) => $"tenant:{tenantId:D}";

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    public override async Task OnConnectedAsync()
    {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var tenantClaim = Context.User?.FindFirstValue("tenant_id");
// LEARN: branches only when the boolean condition is true.
        if (!Guid.TryParse(tenantClaim, out var tenantId))
        {
// LEARN: executes one C# statement; semicolons terminate most statements.
            Context.Abort();
// LEARN: returns a value or exits the current method.
            return;
        }

// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await Groups.AddToGroupAsync(Context.ConnectionId, TenantGroup(tenantId), Context.ConnectionAborted);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await base.OnConnectedAsync();
    }
}
