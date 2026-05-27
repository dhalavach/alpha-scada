using System.Net.Http.Json;
using Alpha.Scada.Contracts;
using Alpha.Scada.Gateway.Application;
using Alpha.Scada.Gateway.Realtime;
using Alpha.Scada.ServiceDefaults;
using Microsoft.AspNetCore.SignalR;

const string serviceName = "alpha-scada-gateway";

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddSignalR();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});
builder.Services.AddHttpClient("identity", client => client.BaseAddress = new Uri(builder.Configuration["Services:Identity"] ?? "http://localhost:5210"));
builder.Services.AddHttpClient("tenant", client => client.BaseAddress = new Uri(builder.Configuration["Services:Tenant"] ?? "http://localhost:5211"));
builder.Services.AddHttpClient("asset", client => client.BaseAddress = new Uri(builder.Configuration["Services:Asset"] ?? "http://localhost:5212"));
builder.Services.AddHttpClient("tagCatalog", client => client.BaseAddress = new Uri(builder.Configuration["Services:TagCatalog"] ?? "http://localhost:5213"));
builder.Services.AddHttpClient("telemetry", client => client.BaseAddress = new Uri(builder.Configuration["Services:Telemetry"] ?? "http://localhost:5214"));
builder.Services.AddHttpClient("alarm", client => client.BaseAddress = new Uri(builder.Configuration["Services:Alarm"] ?? "http://localhost:5215"));
builder.Services.AddHttpClient("reporting", client => client.BaseAddress = new Uri(builder.Configuration["Services:Reporting"] ?? "http://localhost:5216"));

var app = builder.Build();
app.UseCors();

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = serviceName, utc = DateTimeOffset.UtcNow }));
app.MapGet("/ready", () => Results.Ok(new { status = "ready" }));
app.MapGet("/metrics", () => MinimalApi.Metrics(serviceName));

app.MapPost("/api/auth/login", async (LoginRequest request, IHttpClientFactory factory, CancellationToken cancellationToken) =>
{
    var response = await factory.CreateClient("identity").PostAsJsonAsync("/internal/v1/auth/login", request, cancellationToken);
    return response.IsSuccessStatusCode
        ? Results.Ok(await response.Content.ReadFromJsonAsync<LoginResponse>(cancellationToken))
        : Results.Unauthorized();
});

app.MapPost("/api/auth/logout", async (HttpContext context, JwtTokenService tokens, IHttpClientFactory factory) =>
{
    var user = GatewayAuth.Authenticate(context, tokens);
    if (user is null) return Results.Unauthorized();
    var request = new HttpRequestMessage(HttpMethod.Post, "/internal/v1/auth/logout").WithUser(user);
    await factory.CreateClient("identity").SendAsync(request, context.RequestAborted);
    return Results.NoContent();
});

app.MapGet("/api/me", (HttpContext context, JwtTokenService tokens) =>
{
    var user = GatewayAuth.Authenticate(context, tokens);
    return user is null ? Results.Unauthorized() : Results.Ok(user);
});

app.MapGet("/api/tenants", async (HttpContext context, JwtTokenService tokens, IHttpClientFactory factory) =>
{
    var user = GatewayAuth.Authenticate(context, tokens);
    if (user is null) return Results.Unauthorized();
    return await ForwardGetAsync<IReadOnlyCollection<TenantDto>>(factory.CreateClient("tenant"), "/internal/v1/tenants", user, context.RequestAborted);
});

app.MapGet("/api/sites", async (HttpContext context, JwtTokenService tokens, IHttpClientFactory factory) =>
{
    var user = GatewayAuth.Authenticate(context, tokens);
    if (user is null) return Results.Unauthorized();
    return await ForwardGetAsync<IReadOnlyCollection<SiteDto>>(factory.CreateClient("asset"), "/internal/v1/sites", user, context.RequestAborted);
});

app.MapGet("/api/sites/{siteId:guid}/units", async (Guid siteId, HttpContext context, JwtTokenService tokens, IHttpClientFactory factory) =>
{
    var user = GatewayAuth.Authenticate(context, tokens);
    if (user is null) return Results.Unauthorized();
    return await ForwardGetAsync<IReadOnlyCollection<UnitDto>>(factory.CreateClient("asset"), $"/internal/v1/sites/{siteId}/units", user, context.RequestAborted);
});

app.MapGet("/api/units/{unitId:guid}", async (Guid unitId, HttpContext context, JwtTokenService tokens, IHttpClientFactory factory) =>
{
    var user = GatewayAuth.Authenticate(context, tokens);
    if (user is null) return Results.Unauthorized();
    return await ForwardGetAsync<UnitDto>(factory.CreateClient("asset"), $"/internal/v1/units/{unitId}", user, context.RequestAborted);
});

app.MapGet("/api/units/{unitId:guid}/tags/current", async (Guid unitId, HttpContext context, JwtTokenService tokens, IHttpClientFactory factory) =>
{
    var user = GatewayAuth.Authenticate(context, tokens);
    if (user is null) return Results.Unauthorized();

    var tagRequest = new HttpRequestMessage(HttpMethod.Get, $"/internal/v1/units/{unitId}/tags").WithUser(user);
    var currentRequest = new HttpRequestMessage(HttpMethod.Get, $"/internal/v1/telemetry/units/{unitId}/current").WithUser(user);
    var tagResponse = await factory.CreateClient("tagCatalog").SendAsync(tagRequest, context.RequestAborted);
    var currentResponse = await factory.CreateClient("telemetry").SendAsync(currentRequest, context.RequestAborted);
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

app.MapGet("/api/tags/{tagId:guid}/history", async (Guid tagId, int? minutes, HttpContext context, JwtTokenService tokens, IHttpClientFactory factory) =>
{
    var user = GatewayAuth.Authenticate(context, tokens);
    if (user is null) return Results.Unauthorized();
    return await ForwardGetAsync<IReadOnlyCollection<TelemetryHistoryPointDto>>(
        factory.CreateClient("telemetry"),
        $"/internal/v1/telemetry/tags/{tagId}/history?minutes={minutes ?? 30}",
        user,
        context.RequestAborted);
});

app.MapGet("/api/alarms/active", async (HttpContext context, JwtTokenService tokens, IHttpClientFactory factory) =>
{
    var user = GatewayAuth.Authenticate(context, tokens);
    if (user is null) return Results.Unauthorized();
    return await ForwardGetAsync<IReadOnlyCollection<AlarmDto>>(factory.CreateClient("alarm"), "/internal/v1/alarms/active", user, context.RequestAborted);
});

app.MapPost("/api/alarms/{alarmId:guid}/ack", async (Guid alarmId, HttpContext context, JwtTokenService tokens, IHttpClientFactory factory) =>
{
    var user = GatewayAuth.Authenticate(context, tokens);
    if (user is null) return Results.Unauthorized();
    var request = new HttpRequestMessage(HttpMethod.Post, $"/internal/v1/alarms/{alarmId}/ack").WithUser(user);
    var response = await factory.CreateClient("alarm").SendAsync(request, context.RequestAborted);
    return Results.StatusCode((int)response.StatusCode);
});

app.MapGet("/api/reports/monthly", async (HttpContext context, JwtTokenService tokens, IHttpClientFactory factory) =>
{
    var user = GatewayAuth.Authenticate(context, tokens);
    if (user is null) return Results.Unauthorized();
    return await ForwardGetAsync<IReadOnlyCollection<MonthlyReportDto>>(factory.CreateClient("reporting"), "/internal/v1/reports/monthly", user, context.RequestAborted);
});

app.MapPost("/api/reports/monthly/run", async (ReportRunRequest request, HttpContext context, JwtTokenService tokens, IHttpClientFactory factory) =>
{
    var user = GatewayAuth.Authenticate(context, tokens);
    if (user is null) return Results.Unauthorized();
    var downstream = new HttpRequestMessage(HttpMethod.Post, "/internal/v1/reports/monthly/run")
    {
        Content = JsonContent.Create(request)
    }.WithUser(user);
    var response = await factory.CreateClient("reporting").SendAsync(downstream, context.RequestAborted);
    return response.IsSuccessStatusCode
        ? Results.Ok(await response.Content.ReadFromJsonAsync<MonthlyReportDto>(context.RequestAborted))
        : Results.StatusCode((int)response.StatusCode);
});

app.MapPost("/internal/v1/realtime/telemetry-updated", async (RealtimeNotificationRequest request, IHubContext<TelemetryHub> hub, CancellationToken cancellationToken) =>
{
    await hub.Clients.All.SendAsync("telemetryUpdated", request, cancellationToken);
    return Results.NoContent();
});

app.MapPost("/internal/v1/realtime/alarms-changed", async (RealtimeNotificationRequest request, IHubContext<TelemetryHub> hub, CancellationToken cancellationToken) =>
{
    await hub.Clients.All.SendAsync("alarmsChanged", request, cancellationToken);
    return Results.NoContent();
});

app.MapPost("/internal/v1/realtime/unit-status-changed", async (RealtimeNotificationRequest request, IHubContext<TelemetryHub> hub, CancellationToken cancellationToken) =>
{
    await hub.Clients.All.SendAsync("unitStatusChanged", request, cancellationToken);
    return Results.NoContent();
});

app.MapHub<TelemetryHub>("/hubs/telemetry");

app.Run();

static async Task<IResult> ForwardGetAsync<T>(HttpClient client, string path, CurrentUserDto user, CancellationToken cancellationToken)
{
    var request = new HttpRequestMessage(HttpMethod.Get, path).WithUser(user);
    var response = await client.SendAsync(request, cancellationToken);
    return response.IsSuccessStatusCode
        ? Results.Ok(await response.Content.ReadFromJsonAsync<T>(cancellationToken))
        : Results.StatusCode((int)response.StatusCode);
}
