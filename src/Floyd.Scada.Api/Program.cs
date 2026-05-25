using Floyd.Scada.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<ScadaStore>();
builder.Services.AddSingleton<TelemetryWebSocketHub>();
builder.Services.AddHostedService<F60SimulatorWorker>();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});

var app = builder.Build();

app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseWebSockets();

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    service = "floyd-scada-api",
    utc = DateTimeOffset.UtcNow
}));

app.MapGet("/api/units", (ScadaStore store) => Results.Ok(store.GetUnits()));

app.MapGet("/api/tags/current", (ScadaStore store) => Results.Ok(store.GetCurrentTags()));

app.MapGet("/api/tags/{tagKey}/history", (
    string tagKey,
    int? minutes,
    ScadaStore store) =>
{
    var window = TimeSpan.FromMinutes(Math.Clamp(minutes ?? 30, 1, 24 * 60));
    return Results.Ok(store.GetHistory(tagKey, window));
});

app.MapGet("/api/alarms/active", (ScadaStore store) => Results.Ok(store.GetActiveAlarms()));

app.MapGet("/api/reports/monthly", (ScadaStore store) => Results.Ok(store.GetMonthlyReport()));

app.MapGet("/ws/telemetry", async (HttpContext context, TelemetryWebSocketHub hub) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        return Results.BadRequest(new { error = "Expected a WebSocket request." });
    }

    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    await hub.AttachAsync(socket, context.RequestAborted);

    return Results.Empty;
});

app.Run();
