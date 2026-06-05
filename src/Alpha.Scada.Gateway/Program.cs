/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Gateway/Program.cs
- Module role: Alpha.Scada.Gateway is the public boundary/BFF. It keeps the React UI talking to one API surface, owns SignalR realtime fan-out, and translates browser-facing requests into calls or messages for backend services.
- Local role: This is the composition root: it wires configuration, dependency injection, authentication, messaging, migrations, operational endpoints, and HTTP routes for one process.
- Architecture connection: this file participates in the service boundary. Follow the dependency direction from HTTP/message edges inward to application/domain code and outward to infrastructure.
- .NET/C# concepts to notice: Minimal APIs use route lambdas instead of controller classes; services are supplied through dependency injection parameters. Wolverine is the in-process messaging abstraction; it routes commands/events and applies retry, inbox/outbox, and transport policies configured in ServiceDefaults. Authentication/authorization are standard ASP.NET Core concepts: middleware validates tokens, then policies/claims decide access. SignalR provides server-to-browser realtime delivery; Gateway owns that public realtime boundary.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using System.Net.Http.Json;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Contracts;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Gateway;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Gateway.Application;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Gateway.Realtime;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Reporting.Contracts;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.ServiceDefaults;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.ServiceDefaults.Messaging;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Microsoft.AspNetCore.Authentication.JwtBearer;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Wolverine;

// LEARN: declares a compile-time constant; callers cannot change this value.
const string serviceName = "alpha-scada-gateway";

// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
var builder = WebApplication.CreateBuilder(args);
// LEARN: registers a dependency with the built-in .NET dependency-injection container.
builder.Services.AddServiceDatabase(builder.Configuration);
// LEARN: registers a dependency with the built-in .NET dependency-injection container.
builder.Services.AddAlphaJwtAuthentication(builder.Configuration, options =>
{
// LEARN: creates a new object or record instance.
    options.Events = new JwtBearerEvents
    {
// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
        OnMessageReceived = context =>
        {
// LEARN: branches only when the boolean condition is true.
            if (context.HttpContext.Request.Path.StartsWithSegments("/hubs/telemetry")
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
                && context.Request.Query.TryGetValue("access_token", out var accessToken))
            {
// LEARN: executes one C# statement; semicolons terminate most statements.
                context.Token = accessToken.ToString();
            }

// LEARN: returns a value or exits the current method.
            return Task.CompletedTask;
        }
    };
// LEARN: executes one C# statement; semicolons terminate most statements.
});
// LEARN: registers a dependency with the built-in .NET dependency-injection container.
builder.Services.AddSignalR();
// LEARN: registers a dependency with the built-in .NET dependency-injection container.
builder.Services.AddCors(options =>
{
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
    var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
// LEARN: executes one C# statement; semicolons terminate most statements.
        ?? ["http://localhost:5173", "http://localhost:8080"];
// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
    options.AddDefaultPolicy(policy => policy
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
        .WithOrigins(allowedOrigins)
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
        .AllowAnyHeader()
// LEARN: executes one C# statement; semicolons terminate most statements.
        .AllowAnyMethod());
// LEARN: executes one C# statement; semicolons terminate most statements.
});
// LEARN: registers a dependency with the built-in .NET dependency-injection container.
builder.Services.AddAlphaServiceClients(
// LEARN: continues an argument/object/collection initializer onto the next line.
    builder.Configuration,
// LEARN: continues an argument/object/collection initializer onto the next line.
    AlphaServiceClients.Identity,
// LEARN: continues an argument/object/collection initializer onto the next line.
    AlphaServiceClients.Tenant,
// LEARN: continues an argument/object/collection initializer onto the next line.
    AlphaServiceClients.Asset,
// LEARN: continues an argument/object/collection initializer onto the next line.
    AlphaServiceClients.TagCatalog,
// LEARN: continues an argument/object/collection initializer onto the next line.
    AlphaServiceClients.Telemetry,
// LEARN: continues an argument/object/collection initializer onto the next line.
    AlphaServiceClients.Alarm,
// LEARN: executes one C# statement; semicolons terminate most statements.
    AlphaServiceClients.Reporting);
// LEARN: enables the shared Wolverine/NATS messaging setup for this service.
builder.Host.UseAlphaMessaging(serviceName, MessagingTopology.Configure);

// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
var app = builder.Build();
// LEARN: executes one C# statement; semicolons terminate most statements.
app.UseCors();
// LEARN: executes one C# statement; semicolons terminate most statements.
app.UseAlphaAuthorization();
// LEARN: executes one C# statement; semicolons terminate most statements.
app.MapAlphaOperationalEndpoints(serviceName);

// LEARN: registers an HTTP POST endpoint in ASP.NET Core Minimal APIs.
app.MapPost("/api/auth/login", async (LoginRequest request, IHttpClientFactory factory, CancellationToken cancellationToken) =>
{
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
    var response = await factory.CreateClient(AlphaServiceClients.Identity).PostAsJsonAsync("/internal/v1/auth/login", request, cancellationToken);
// LEARN: returns a value or exits the current method.
    return response.IsSuccessStatusCode
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
        ? Results.Ok(await response.Content.ReadFromJsonAsync<LoginResponse>(cancellationToken))
// LEARN: executes one C# statement; semicolons terminate most statements.
        : Results.Unauthorized();
// LEARN: executes one C# statement; semicolons terminate most statements.
});

// LEARN: attaches ASP.NET Core authorization so callers need an accepted authenticated user/policy.
var api = app.MapGroup("/api").RequireAuthorization();

// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
api.MapPost("/auth/logout", async (HttpContext context, IHttpClientFactory factory) =>
{
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
    var request = new HttpRequestMessage(HttpMethod.Post, "/internal/v1/auth/logout").WithBearerToken(context);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
    await factory.CreateClient(AlphaServiceClients.Identity).SendAsync(request, context.RequestAborted);
// LEARN: returns a value or exits the current method.
    return Results.NoContent();
// LEARN: executes one C# statement; semicolons terminate most statements.
});

// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
api.MapGet("/me", (AuthenticatedUser user) => Results.Ok(user.Current));

// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
api.MapGet("/tenants", async (IHttpClientFactory factory, HttpContext context) =>
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
    await ForwardGetAsync<IReadOnlyCollection<TenantDto>>(factory.CreateClient(AlphaServiceClients.Tenant), "/internal/v1/tenants", context, context.RequestAborted));

// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
api.MapGet("/sites", async (IHttpClientFactory factory, HttpContext context) =>
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
    await ForwardGetAsync<IReadOnlyCollection<SiteDto>>(factory.CreateClient(AlphaServiceClients.Asset), "/internal/v1/sites", context, context.RequestAborted));

// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
api.MapGet("/sites/{siteId:guid}/units", async (Guid siteId, IHttpClientFactory factory, HttpContext context) =>
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
    await ForwardGetAsync<IReadOnlyCollection<UnitDto>>(factory.CreateClient(AlphaServiceClients.Asset), $"/internal/v1/sites/{siteId}/units", context, context.RequestAborted));

// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
api.MapGet("/units/{unitId:guid}", async (Guid unitId, IHttpClientFactory factory, HttpContext context) =>
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
    await ForwardGetAsync<UnitDto>(factory.CreateClient(AlphaServiceClients.Asset), $"/internal/v1/units/{unitId}", context, context.RequestAborted));

// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
api.MapGet("/units/{unitId:guid}/tags/current", async (Guid unitId, HttpContext context, IHttpClientFactory factory) =>
{
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
    var tagRequest = new HttpRequestMessage(HttpMethod.Get, $"/internal/v1/units/{unitId}/tags").WithBearerToken(context);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
    var currentRequest = new HttpRequestMessage(HttpMethod.Get, $"/internal/v1/telemetry/units/{unitId}/current").WithBearerToken(context);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
    var tagResponse = await factory.CreateClient(AlphaServiceClients.TagCatalog).SendAsync(tagRequest, context.RequestAborted);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
    var currentResponse = await factory.CreateClient(AlphaServiceClients.Telemetry).SendAsync(currentRequest, context.RequestAborted);
// LEARN: branches only when the boolean condition is true.
    if (!tagResponse.IsSuccessStatusCode || !currentResponse.IsSuccessStatusCode)
    {
// LEARN: returns a value or exits the current method.
        return Results.StatusCode(502);
    }

// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
    var tags = await tagResponse.Content.ReadFromJsonAsync<IReadOnlyCollection<TagDto>>(context.RequestAborted) ?? [];
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
    var values = await currentResponse.Content.ReadFromJsonAsync<IReadOnlyCollection<TagValueDto>>(context.RequestAborted) ?? [];
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
    var valueByTag = values.ToDictionary(value => value.TagId);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
    var result = tags.Select(tag =>
    {
// LEARN: executes one C# statement; semicolons terminate most statements.
        valueByTag.TryGetValue(tag.Id, out var current);
// LEARN: returns a value or exits the current method.
        return new TagCurrentDto(
// LEARN: continues an argument/object/collection initializer onto the next line.
            tag.Id,
// LEARN: continues an argument/object/collection initializer onto the next line.
            tag.TenantId,
// LEARN: continues an argument/object/collection initializer onto the next line.
            tag.UnitId,
// LEARN: continues an argument/object/collection initializer onto the next line.
            tag.Key,
// LEARN: continues an argument/object/collection initializer onto the next line.
            tag.Name,
// LEARN: continues an argument/object/collection initializer onto the next line.
            tag.Subsystem,
// LEARN: continues an argument/object/collection initializer onto the next line.
            current?.Value ?? 0,
// LEARN: continues an argument/object/collection initializer onto the next line.
            tag.EngineeringUnit,
// LEARN: continues an argument/object/collection initializer onto the next line.
            current?.Quality ?? "stale",
// LEARN: executes one C# statement; semicolons terminate most statements.
            current?.TimestampUtc ?? DateTimeOffset.MinValue);
// LEARN: executes one C# statement; semicolons terminate most statements.
    }).ToArray();
// LEARN: returns a value or exits the current method.
    return Results.Ok(result);
// LEARN: executes one C# statement; semicolons terminate most statements.
});

// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
api.MapGet("/tags/{tagId:guid}/history", async (Guid tagId, int? minutes, IHttpClientFactory factory, HttpContext context) =>
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
    await ForwardGetAsync<IReadOnlyCollection<TelemetryHistoryPointDto>>(
// LEARN: continues an argument/object/collection initializer onto the next line.
        factory.CreateClient(AlphaServiceClients.Telemetry),
// LEARN: continues an argument/object/collection initializer onto the next line.
        $"/internal/v1/telemetry/tags/{tagId}/history?minutes={minutes ?? 30}",
// LEARN: continues an argument/object/collection initializer onto the next line.
        context,
// LEARN: executes one C# statement; semicolons terminate most statements.
        context.RequestAborted));

// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
api.MapGet("/alarms/active", async (IHttpClientFactory factory, HttpContext context) =>
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
    await ForwardGetAsync<IReadOnlyCollection<AlarmDto>>(factory.CreateClient(AlphaServiceClients.Alarm), "/internal/v1/alarms/active", context, context.RequestAborted));

// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
api.MapPost("/alarms/{alarmId:guid}/ack", async (Guid alarmId, HttpContext context, IHttpClientFactory factory) =>
{
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
    var request = new HttpRequestMessage(HttpMethod.Post, $"/internal/v1/alarms/{alarmId}/ack").WithBearerToken(context);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
    var response = await factory.CreateClient(AlphaServiceClients.Alarm).SendAsync(request, context.RequestAborted);
// LEARN: returns a value or exits the current method.
    return Results.StatusCode((int)response.StatusCode);
// LEARN: executes one C# statement; semicolons terminate most statements.
});

// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
api.MapGet("/reports/monthly", async (IHttpClientFactory factory, HttpContext context) =>
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
    await ForwardGetAsync<IReadOnlyCollection<MonthlyReportDto>>(factory.CreateClient(AlphaServiceClients.Reporting), "/internal/v1/reports/monthly", context, context.RequestAborted));

// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
api.MapPost("/reports/monthly/run", async (ReportRunRequest request, AuthenticatedUser user, HttpContext context, IHttpClientFactory factory, IMessageBus bus) =>
{
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
    var unitRequest = new HttpRequestMessage(HttpMethod.Get, $"/internal/v1/units/{request.UnitId}").WithBearerToken(context);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
    var unitResponse = await factory.CreateClient(AlphaServiceClients.Asset).SendAsync(unitRequest, context.RequestAborted);
// LEARN: branches only when the boolean condition is true.
    if (!unitResponse.IsSuccessStatusCode)
    {
// LEARN: returns a value or exits the current method.
        return Results.StatusCode((int)unitResponse.StatusCode);
    }

// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
    var unit = await unitResponse.Content.ReadFromJsonAsync<UnitDto>(context.RequestAborted);
// LEARN: branches only when the boolean condition is true.
    if (unit is null)
    {
// LEARN: returns a value or exits the current method.
        return Results.StatusCode(502);
    }

// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
    var jobId = Guid.NewGuid();
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
    var period = string.IsNullOrWhiteSpace(request.Period) ? DateTimeOffset.UtcNow.ToString("yyyy-MM") : request.Period;
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
    await bus.PublishAsync(new ReportRequested(
// LEARN: continues an argument/object/collection initializer onto the next line.
        jobId,
// LEARN: continues an argument/object/collection initializer onto the next line.
        unit.TenantId,
// LEARN: continues an argument/object/collection initializer onto the next line.
        unit.Id,
// LEARN: continues an argument/object/collection initializer onto the next line.
        period,
// LEARN: continues an argument/object/collection initializer onto the next line.
        user.Current.UserId,
// LEARN: continues an argument/object/collection initializer onto the next line.
        DateTimeOffset.UtcNow,
// LEARN: executes one C# statement; semicolons terminate most statements.
        ServiceIdentity.CurrentCorrelationId()));

// LEARN: returns a value or exits the current method.
    return Results.Accepted($"/api/reports/monthly?jobId={jobId}", new { jobId, status = "queued" });
// LEARN: executes one C# statement; semicolons terminate most statements.
});

// LEARN: attaches ASP.NET Core authorization so callers need an accepted authenticated user/policy.
app.MapHub<TelemetryHub>("/hubs/telemetry").RequireAuthorization();

// LEARN: executes one C# statement; semicolons terminate most statements.
app.Run();

// LEARN: performs an outbound HTTP call, usually to another service boundary.
static async Task<IResult> ForwardGetAsync<T>(HttpClient client, string path, HttpContext context, CancellationToken cancellationToken)
{
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
    var request = new HttpRequestMessage(HttpMethod.Get, path).WithBearerToken(context);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
    var response = await client.SendAsync(request, cancellationToken);
// LEARN: returns a value or exits the current method.
    return response.IsSuccessStatusCode
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
        ? Results.Ok(await response.Content.ReadFromJsonAsync<T>(cancellationToken))
// LEARN: executes one C# statement; semicolons terminate most statements.
        : Results.StatusCode((int)response.StatusCode);
}
