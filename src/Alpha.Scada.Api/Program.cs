using Alpha.Scada.Api.Contracts;
using Alpha.Scada.Api.Data;
using Alpha.Scada.Api.Modules.Auth;
using Alpha.Scada.Api.Modules.EdgeIngestion;
using Alpha.Scada.Api.Modules.Realtime;
using Alpha.Scada.Api.Modules.Telemetry;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDatabase(builder.Configuration);
builder.Services.AddSingleton<AuthService>();
builder.Services.AddSignalR();
builder.Services.AddHostedService<MqttIngestionWorker>();
builder.Services.AddHostedService<AlarmEvaluationWorker>();
builder.Services.AddHostedService<ChpUnitDatabaseSimulatorWorker>();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});

var app = builder.Build();

await app.Services.GetRequiredService<DatabaseMigrator>().MigrateAsync(CancellationToken.None);

app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    service = "alpha-scada-api",
    utc = DateTimeOffset.UtcNow
}));

app.MapGet("/ready", async (NpgsqlDataSource dataSource, CancellationToken cancellationToken) =>
{
    await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
    await using var command = new NpgsqlCommand("select 1", connection);
    await command.ExecuteScalarAsync(cancellationToken);
    return Results.Ok(new { status = "ready" });
});

app.MapGet("/metrics", () => Results.Text("""
    # HELP alpha_scada_up Application availability
    # TYPE alpha_scada_up gauge
    alpha_scada_up 1
    """, "text/plain"));

app.MapPost("/api/auth/login", async (LoginRequest request, AuthService auth, CancellationToken cancellationToken) =>
{
    var response = await auth.LoginAsync(request, cancellationToken);
    return response is null ? Results.Unauthorized() : Results.Ok(response);
});

app.MapPost("/api/auth/logout", async (HttpContext context, AuthService auth) =>
{
    return await AuthEndpointFilter.WithUser(context, auth, async user =>
    {
        var token = context.Request.Headers.Authorization.ToString()["Bearer ".Length..].Trim();
        await auth.LogoutAsync(token, user, context.RequestAborted);
        return Results.NoContent();
    });
});

app.MapGet("/api/me", async (HttpContext context, AuthService auth) =>
{
    return await AuthEndpointFilter.WithUser(context, auth, user => Task.FromResult<IResult>(Results.Ok(user)));
});

app.MapGet("/api/tenants", async (HttpContext context, AuthService auth, PlatformRepository repository) =>
{
    return await AuthEndpointFilter.WithUser(context, auth, async user =>
        Results.Ok(await repository.GetTenantsAsync(user, context.RequestAborted)));
});

app.MapGet("/api/sites", async (HttpContext context, AuthService auth, PlatformRepository repository) =>
{
    return await AuthEndpointFilter.WithUser(context, auth, async user =>
        Results.Ok(await repository.GetSitesAsync(user, context.RequestAborted)));
});

app.MapGet("/api/sites/{siteId:guid}/units", async (Guid siteId, HttpContext context, AuthService auth, PlatformRepository repository) =>
{
    return await AuthEndpointFilter.WithUser(context, auth, async user =>
        Results.Ok(await repository.GetUnitsForSiteAsync(siteId, user, context.RequestAborted)));
});

app.MapGet("/api/units/{unitId:guid}", async (Guid unitId, HttpContext context, AuthService auth, PlatformRepository repository) =>
{
    return await AuthEndpointFilter.WithUser(context, auth, async user =>
    {
        var unit = await repository.GetUnitAsync(unitId, user, context.RequestAborted);
        return unit is null ? Results.NotFound() : Results.Ok(unit);
    });
});

app.MapGet("/api/units/{unitId:guid}/tags/current", async (Guid unitId, HttpContext context, AuthService auth, PlatformRepository repository) =>
{
    return await AuthEndpointFilter.WithUser(context, auth, async user =>
        Results.Ok(await repository.GetCurrentTagsAsync(unitId, user, context.RequestAborted)));
});

app.MapGet("/api/tags/{tagId:guid}/history", async (
    Guid tagId,
    int? minutes,
    HttpContext context,
    AuthService auth,
    PlatformRepository repository) =>
{
    return await AuthEndpointFilter.WithUser(context, auth, async user =>
    {
        var window = TimeSpan.FromMinutes(Math.Clamp(minutes ?? 30, 1, 24 * 60));
        return Results.Ok(await repository.GetHistoryAsync(tagId, window, user, context.RequestAborted));
    });
});

app.MapGet("/api/alarms/active", async (HttpContext context, AuthService auth, PlatformRepository repository) =>
{
    return await AuthEndpointFilter.WithUser(context, auth, async user =>
        Results.Ok(await repository.GetActiveAlarmsAsync(user, context.RequestAborted)));
});

app.MapPost("/api/alarms/{alarmId:guid}/ack", async (Guid alarmId, HttpContext context, AuthService auth, PlatformRepository repository) =>
{
    return await AuthEndpointFilter.WithUser(context, auth, async user =>
    {
        if (!AuthEndpointFilter.CanAcknowledge(user))
        {
            return Results.Forbid();
        }

        var changed = await repository.AcknowledgeAlarmAsync(alarmId, user, context.RequestAborted);
        return changed ? Results.NoContent() : Results.NotFound();
    });
});

app.MapGet("/api/reports/monthly", async (HttpContext context, AuthService auth, PlatformRepository repository) =>
{
    return await AuthEndpointFilter.WithUser(context, auth, async user =>
        Results.Ok(await repository.GetMonthlyReportsAsync(user, context.RequestAborted)));
});

app.MapPost("/api/reports/monthly/run", async (
    ReportRunRequest request,
    HttpContext context,
    AuthService auth,
    PlatformRepository repository) =>
{
    return await AuthEndpointFilter.WithUser(context, auth, async user =>
        Results.Ok(await repository.GenerateMonthlyReportAsync(request.UnitId, request.Period, user, context.RequestAborted)));
});

app.MapHub<TelemetryHub>("/hubs/telemetry");

app.Run();
