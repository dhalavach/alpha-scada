/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Gateway/Program.cs
- Module role: Alpha.Scada.Gateway is the public boundary/BFF. It keeps the React UI talking to one API surface, owns SignalR realtime fan-out, and translates browser-facing requests into calls or messages for backend services.
- Local role: This is the composition root: it wires configuration, dependency injection, authentication, messaging, migrations, operational endpoints, and HTTP routes for one process.
- Architecture connection: this file participates in the service boundary. Follow the dependency direction from HTTP/message edges inward to application/domain code and outward to infrastructure.
- .NET/C# concepts to notice: Minimal APIs use route lambdas instead of controller classes; services are supplied through dependency injection parameters. Wolverine is the in-process messaging abstraction; it routes commands/events and applies retry, inbox/outbox, and transport policies configured in ServiceDefaults. Authentication/authorization are standard ASP.NET Core concepts: middleware validates tokens, then policies/claims decide access. SignalR provides server-to-browser realtime delivery; Gateway owns that public realtime boundary.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

using System.Net.Http.Json;
using Alpha.Scada.Contracts;
using Alpha.Scada.Gateway;
using Alpha.Scada.Gateway.Application;
using Alpha.Scada.Gateway.Realtime;
using Alpha.Scada.Reporting.Contracts;
using Alpha.Scada.ServiceDefaults;
using Alpha.Scada.ServiceDefaults.Messaging;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Wolverine;

const string serviceName = "alpha-scada-gateway";

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddServiceDatabase(builder.Configuration);
builder.Services.AddAlphaJwtAuthentication(builder.Configuration, options =>
{
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            if (context.HttpContext.Request.Path.StartsWithSegments("/hubs/telemetry")
                && context.Request.Query.TryGetValue("access_token", out var accessToken))
            {
                context.Token = accessToken.ToString();
            }

            return Task.CompletedTask;
        }
    };
});
builder.Services.AddSignalR();
builder.Services.AddCors(options =>
{
    var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
        ?? ["http://localhost:5173", "http://localhost:8080"];
    options.AddDefaultPolicy(policy => policy
        .WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod());
});
builder.Services.AddAlphaServiceClients(
    builder.Configuration,
    AlphaServiceClients.Identity,
    AlphaServiceClients.Tenant,
    AlphaServiceClients.Asset,
    AlphaServiceClients.TagCatalog,
    AlphaServiceClients.Telemetry,
    AlphaServiceClients.Alarm,
    AlphaServiceClients.Reporting);
builder.Host.UseAlphaMessaging(serviceName, MessagingTopology.Configure);

var app = builder.Build();
app.UseCors();
app.UseAlphaAuthorization();
app.MapAlphaOperationalEndpoints(serviceName);

app.MapPost("/api/auth/login", async (LoginRequest request, IHttpClientFactory factory, CancellationToken cancellationToken) =>
{
    var response = await factory.CreateClient(AlphaServiceClients.Identity).PostAsJsonAsync("/internal/v1/auth/login", request, cancellationToken);
    return response.IsSuccessStatusCode
        ? Results.Ok(await response.Content.ReadFromJsonAsync<LoginResponse>(cancellationToken))
        : Results.Unauthorized();
});

var api = app.MapGroup("/api").RequireAuthorization();

api.MapPost("/auth/logout", async (HttpContext context, IHttpClientFactory factory) =>
{
    var request = new HttpRequestMessage(HttpMethod.Post, "/internal/v1/auth/logout").WithBearerToken(context);
    await factory.CreateClient(AlphaServiceClients.Identity).SendAsync(request, context.RequestAborted);
    return Results.NoContent();
});

api.MapGet("/me", (AuthenticatedUser user) => Results.Ok(user.Current));

api.MapGet("/tenants", async (IHttpClientFactory factory, HttpContext context) =>
    await ForwardGetAsync<IReadOnlyCollection<TenantDto>>(factory.CreateClient(AlphaServiceClients.Tenant), "/internal/v1/tenants", context, context.RequestAborted));

api.MapGet("/sites", async (IHttpClientFactory factory, HttpContext context) =>
    await ForwardGetAsync<IReadOnlyCollection<SiteDto>>(factory.CreateClient(AlphaServiceClients.Asset), "/internal/v1/sites", context, context.RequestAborted));

api.MapGet("/sites/{siteId:guid}/units", async (Guid siteId, IHttpClientFactory factory, HttpContext context) =>
    await ForwardGetAsync<IReadOnlyCollection<UnitDto>>(factory.CreateClient(AlphaServiceClients.Asset), $"/internal/v1/sites/{siteId}/units", context, context.RequestAborted));

api.MapGet("/units/{unitId:guid}", async (Guid unitId, IHttpClientFactory factory, HttpContext context) =>
    await ForwardGetAsync<UnitDto>(factory.CreateClient(AlphaServiceClients.Asset), $"/internal/v1/units/{unitId}", context, context.RequestAborted));

api.MapGet("/units/{unitId:guid}/tags/current", async (Guid unitId, HttpContext context, IHttpClientFactory factory) =>
{
    var tagRequest = new HttpRequestMessage(HttpMethod.Get, $"/internal/v1/units/{unitId}/tags").WithBearerToken(context);
    var currentRequest = new HttpRequestMessage(HttpMethod.Get, $"/internal/v1/telemetry/units/{unitId}/current").WithBearerToken(context);
    var tagResponse = await factory.CreateClient(AlphaServiceClients.TagCatalog).SendAsync(tagRequest, context.RequestAborted);
    var currentResponse = await factory.CreateClient(AlphaServiceClients.Telemetry).SendAsync(currentRequest, context.RequestAborted);
    if (!tagResponse.IsSuccessStatusCode || !currentResponse.IsSuccessStatusCode)
    {
        return Results.StatusCode(502);
    }

    var tags = await tagResponse.Content.ReadFromJsonAsync<IReadOnlyCollection<TagDto>>(context.RequestAborted) ?? [];
    var values = await currentResponse.Content.ReadFromJsonAsync<IReadOnlyCollection<TagValueDto>>(context.RequestAborted) ?? [];
    var valueByTag = values.ToDictionary(value => value.TagId);
    var result = tags.Select(tag =>
    {
        valueByTag.TryGetValue(tag.Id, out var current);
        return new TagCurrentDto(
            tag.Id,
            tag.TenantId,
            tag.UnitId,
            tag.Key,
            tag.Name,
            tag.Subsystem,
            current?.Value ?? 0,
            tag.EngineeringUnit,
            current?.Quality ?? "stale",
            current?.TimestampUtc ?? DateTimeOffset.MinValue);
    }).ToArray();
    return Results.Ok(result);
});

api.MapGet("/tags/{tagId:guid}/history", async (Guid tagId, int? minutes, IHttpClientFactory factory, HttpContext context) =>
    await ForwardGetAsync<IReadOnlyCollection<TelemetryHistoryPointDto>>(
        factory.CreateClient(AlphaServiceClients.Telemetry),
        $"/internal/v1/telemetry/tags/{tagId}/history?minutes={minutes ?? 30}",
        context,
        context.RequestAborted));

api.MapGet("/alarms/active", async (IHttpClientFactory factory, HttpContext context) =>
    await ForwardGetAsync<IReadOnlyCollection<AlarmDto>>(factory.CreateClient(AlphaServiceClients.Alarm), "/internal/v1/alarms/active", context, context.RequestAborted));

api.MapPost("/alarms/{alarmId:guid}/ack", async (Guid alarmId, HttpContext context, IHttpClientFactory factory) =>
{
    var request = new HttpRequestMessage(HttpMethod.Post, $"/internal/v1/alarms/{alarmId}/ack").WithBearerToken(context);
    var response = await factory.CreateClient(AlphaServiceClients.Alarm).SendAsync(request, context.RequestAborted);
    return Results.StatusCode((int)response.StatusCode);
});

api.MapGet("/reports/monthly", async (IHttpClientFactory factory, HttpContext context) =>
    await ForwardGetAsync<IReadOnlyCollection<MonthlyReportDto>>(factory.CreateClient(AlphaServiceClients.Reporting), "/internal/v1/reports/monthly", context, context.RequestAborted));

api.MapPost("/reports/monthly/run", async (ReportRunRequest request, AuthenticatedUser user, HttpContext context, IHttpClientFactory factory, IMessageBus bus) =>
{
    var unitRequest = new HttpRequestMessage(HttpMethod.Get, $"/internal/v1/units/{request.UnitId}").WithBearerToken(context);
    var unitResponse = await factory.CreateClient(AlphaServiceClients.Asset).SendAsync(unitRequest, context.RequestAborted);
    if (!unitResponse.IsSuccessStatusCode)
    {
        return Results.StatusCode((int)unitResponse.StatusCode);
    }

    var unit = await unitResponse.Content.ReadFromJsonAsync<UnitDto>(context.RequestAborted);
    if (unit is null)
    {
        return Results.StatusCode(502);
    }

    var jobId = Guid.NewGuid();
    var period = string.IsNullOrWhiteSpace(request.Period) ? DateTimeOffset.UtcNow.ToString("yyyy-MM") : request.Period;
    await bus.PublishAsync(new ReportRequested(
        jobId,
        unit.TenantId,
        unit.Id,
        period,
        user.Current.UserId,
        DateTimeOffset.UtcNow,
        ServiceIdentity.CurrentCorrelationId()));

    return Results.Accepted($"/api/reports/monthly?jobId={jobId}", new { jobId, status = "queued" });
});

app.MapHub<TelemetryHub>("/hubs/telemetry").RequireAuthorization();

app.Run();

static async Task<IResult> ForwardGetAsync<T>(HttpClient client, string path, HttpContext context, CancellationToken cancellationToken)
{
    var request = new HttpRequestMessage(HttpMethod.Get, path).WithBearerToken(context);
    var response = await client.SendAsync(request, cancellationToken);
    return response.IsSuccessStatusCode
        ? Results.Ok(await response.Content.ReadFromJsonAsync<T>(cancellationToken))
        : Results.StatusCode((int)response.StatusCode);
}
