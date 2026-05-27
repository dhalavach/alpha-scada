using Alpha.Scada.Contracts;
using Alpha.Scada.ServiceDefaults;
using Alpha.Scada.Telemetry.Application;
using Alpha.Scada.Telemetry.Infrastructure;

const string serviceName = "alpha-scada-telemetry";

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddServiceDatabase(builder.Configuration);
builder.Services.AddSingleton<TelemetryMigrator>();
builder.Services.AddSingleton<TelemetryRepository>();
builder.Services.AddSingleton<TelemetryService>();
builder.Services.AddSingleton<JwtTokenService>();

var app = builder.Build();
await app.Services.GetRequiredService<TelemetryMigrator>().MigrateAsync(CancellationToken.None);

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = serviceName, utc = DateTimeOffset.UtcNow }));
app.MapGet("/ready", MinimalApi.ReadyAsync);
app.MapGet("/metrics", () => MinimalApi.Metrics(serviceName));

app.MapPost("/internal/v1/telemetry/ingest", async (TelemetryIngestRequest request, TelemetryService service, CancellationToken cancellationToken) =>
{
    await service.IngestAsync(request, cancellationToken);
    return Results.NoContent();
});

app.MapGet("/internal/v1/telemetry/units/{unitId:guid}/current", async (Guid unitId, HttpContext context, JwtTokenService tokens, TelemetryService service) =>
{
    var user = HttpUserContext.FromBearerToken(context.Request.Headers, tokens);
    return user is null ? Results.Unauthorized() : Results.Ok(await service.GetCurrentAsync(unitId, user, context.RequestAborted));
});

app.MapGet("/internal/v1/telemetry/tags/{tagId:guid}/history", async (Guid tagId, int? minutes, HttpContext context, JwtTokenService tokens, TelemetryService service) =>
{
    var user = HttpUserContext.FromBearerToken(context.Request.Headers, tokens);
    if (user is null) return Results.Unauthorized();
    var window = TimeSpan.FromMinutes(Math.Clamp(minutes ?? 30, 1, 24 * 60));
    return Results.Ok(await service.GetHistoryAsync(tagId, window, user, context.RequestAborted));
});

app.MapGet("/internal/v1/telemetry/units/{unitId:guid}/report-aggregate", async (Guid unitId, string period, TelemetryService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.GetReportAggregateAsync(unitId, period, cancellationToken)));

app.Run();
