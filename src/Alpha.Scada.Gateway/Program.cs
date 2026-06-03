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
builder.Services.AddHttpClient("identity", client => client.BaseAddress = new Uri(builder.Configuration["Services:Identity"] ?? "http://localhost:5210")).AddAlphaResilience();
builder.Services.AddHttpClient("tenant", client => client.BaseAddress = new Uri(builder.Configuration["Services:Tenant"] ?? "http://localhost:5211")).AddAlphaResilience();
builder.Services.AddHttpClient("asset", client => client.BaseAddress = new Uri(builder.Configuration["Services:Asset"] ?? "http://localhost:5212")).AddAlphaResilience();
builder.Services.AddHttpClient("tagCatalog", client => client.BaseAddress = new Uri(builder.Configuration["Services:TagCatalog"] ?? "http://localhost:5213")).AddAlphaResilience();
builder.Services.AddHttpClient("telemetry", client => client.BaseAddress = new Uri(builder.Configuration["Services:Telemetry"] ?? "http://localhost:5214")).AddAlphaResilience();
builder.Services.AddHttpClient("alarm", client => client.BaseAddress = new Uri(builder.Configuration["Services:Alarm"] ?? "http://localhost:5215")).AddAlphaResilience();
builder.Services.AddHttpClient("reporting", client => client.BaseAddress = new Uri(builder.Configuration["Services:Reporting"] ?? "http://localhost:5216")).AddAlphaResilience();
builder.Host.UseAlphaMessaging(serviceName, MessagingTopology.Configure);

var app = builder.Build();
app.UseCors();
app.UseAlphaAuthorization();
app.MapAlphaOperationalEndpoints(serviceName);

app.MapPost("/api/auth/login", async (LoginRequest request, IHttpClientFactory factory, CancellationToken cancellationToken) =>
{
    var response = await factory.CreateClient("identity").PostAsJsonAsync("/internal/v1/auth/login", request, cancellationToken);
    return response.IsSuccessStatusCode
        ? Results.Ok(await response.Content.ReadFromJsonAsync<LoginResponse>(cancellationToken))
        : Results.Unauthorized();
});

var api = app.MapGroup("/api").RequireAuthorization();

api.MapPost("/auth/logout", async (HttpContext context, IHttpClientFactory factory) =>
{
    var request = new HttpRequestMessage(HttpMethod.Post, "/internal/v1/auth/logout").WithBearerToken(context);
    await factory.CreateClient("identity").SendAsync(request, context.RequestAborted);
    return Results.NoContent();
});

api.MapGet("/me", (AuthenticatedUser user) => Results.Ok(user.Current));

api.MapGet("/tenants", async (IHttpClientFactory factory, HttpContext context) =>
    await ForwardGetAsync<IReadOnlyCollection<TenantDto>>(factory.CreateClient("tenant"), "/internal/v1/tenants", context, context.RequestAborted));

api.MapGet("/sites", async (IHttpClientFactory factory, HttpContext context) =>
    await ForwardGetAsync<IReadOnlyCollection<SiteDto>>(factory.CreateClient("asset"), "/internal/v1/sites", context, context.RequestAborted));

api.MapGet("/sites/{siteId:guid}/units", async (Guid siteId, IHttpClientFactory factory, HttpContext context) =>
    await ForwardGetAsync<IReadOnlyCollection<UnitDto>>(factory.CreateClient("asset"), $"/internal/v1/sites/{siteId}/units", context, context.RequestAborted));

api.MapGet("/units/{unitId:guid}", async (Guid unitId, IHttpClientFactory factory, HttpContext context) =>
    await ForwardGetAsync<UnitDto>(factory.CreateClient("asset"), $"/internal/v1/units/{unitId}", context, context.RequestAborted));

api.MapGet("/units/{unitId:guid}/tags/current", async (Guid unitId, HttpContext context, IHttpClientFactory factory) =>
{
    var tagRequest = new HttpRequestMessage(HttpMethod.Get, $"/internal/v1/units/{unitId}/tags").WithBearerToken(context);
    var currentRequest = new HttpRequestMessage(HttpMethod.Get, $"/internal/v1/telemetry/units/{unitId}/current").WithBearerToken(context);
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

api.MapGet("/tags/{tagId:guid}/history", async (Guid tagId, int? minutes, IHttpClientFactory factory, HttpContext context) =>
    await ForwardGetAsync<IReadOnlyCollection<TelemetryHistoryPointDto>>(
        factory.CreateClient("telemetry"),
        $"/internal/v1/telemetry/tags/{tagId}/history?minutes={minutes ?? 30}",
        context,
        context.RequestAborted));

api.MapGet("/alarms/active", async (IHttpClientFactory factory, HttpContext context) =>
    await ForwardGetAsync<IReadOnlyCollection<AlarmDto>>(factory.CreateClient("alarm"), "/internal/v1/alarms/active", context, context.RequestAborted));

api.MapPost("/alarms/{alarmId:guid}/ack", async (Guid alarmId, HttpContext context, IHttpClientFactory factory) =>
{
    var request = new HttpRequestMessage(HttpMethod.Post, $"/internal/v1/alarms/{alarmId}/ack").WithBearerToken(context);
    var response = await factory.CreateClient("alarm").SendAsync(request, context.RequestAborted);
    return Results.StatusCode((int)response.StatusCode);
});

api.MapGet("/reports/monthly", async (IHttpClientFactory factory, HttpContext context) =>
    await ForwardGetAsync<IReadOnlyCollection<MonthlyReportDto>>(factory.CreateClient("reporting"), "/internal/v1/reports/monthly", context, context.RequestAborted));

api.MapPost("/reports/monthly/run", async (ReportRunRequest request, AuthenticatedUser user, HttpContext context, IHttpClientFactory factory, IMessageBus bus) =>
{
    var unitRequest = new HttpRequestMessage(HttpMethod.Get, $"/internal/v1/units/{request.UnitId}").WithBearerToken(context);
    var unitResponse = await factory.CreateClient("asset").SendAsync(unitRequest, context.RequestAborted);
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
