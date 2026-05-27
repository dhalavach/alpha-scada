using Alpha.Scada.Edge.Application;
using Alpha.Scada.Edge.Infrastructure;
using Alpha.Scada.ServiceDefaults;

const string serviceName = "alpha-scada-edge";

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddServiceDatabase(builder.Configuration);
builder.Services.AddSingleton<EdgeMigrator>();
builder.Services.AddSingleton<EdgeTelemetryPipeline>();
builder.Services.AddHostedService<MqttIngestionWorker>();
builder.Services.AddHostedService<ChpUnitSimulatorWorker>();
builder.Services.AddHostedService<CommunicationLossMonitorWorker>();
builder.Services.AddHttpClient("tenant", client => client.BaseAddress = new Uri(builder.Configuration["Services:Tenant"] ?? "http://localhost:5211"));
builder.Services.AddHttpClient("asset", client => client.BaseAddress = new Uri(builder.Configuration["Services:Asset"] ?? "http://localhost:5212"));
builder.Services.AddHttpClient("tagCatalog", client => client.BaseAddress = new Uri(builder.Configuration["Services:TagCatalog"] ?? "http://localhost:5213"));
builder.Services.AddHttpClient("telemetry", client => client.BaseAddress = new Uri(builder.Configuration["Services:Telemetry"] ?? "http://localhost:5214"));
builder.Services.AddHttpClient("alarm", client => client.BaseAddress = new Uri(builder.Configuration["Services:Alarm"] ?? "http://localhost:5215"));
builder.Services.AddHttpClient("gateway", client => client.BaseAddress = new Uri(builder.Configuration["Services:Gateway"] ?? "http://localhost:5202"));

var app = builder.Build();
await app.Services.GetRequiredService<EdgeMigrator>().MigrateAsync(CancellationToken.None);

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = serviceName, utc = DateTimeOffset.UtcNow }));
app.MapGet("/ready", MinimalApi.ReadyAsync);
app.MapGet("/metrics", () => MinimalApi.Metrics(serviceName));

app.Run();
