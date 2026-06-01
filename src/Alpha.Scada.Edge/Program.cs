using Alpha.Scada.Edge.Application;
using Alpha.Scada.Edge.Infrastructure;
using Alpha.Scada.ServiceDefaults;

const string serviceName = "alpha-scada-edge";

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddServiceDatabase(builder.Configuration);
builder.Services.AddSingleton<EdgeMigrator>();
builder.Services.AddHostedService<ChpUnitSimulatorWorker>();

var app = builder.Build();
await app.Services.GetRequiredService<EdgeMigrator>().MigrateAsync(CancellationToken.None);

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = serviceName, utc = DateTimeOffset.UtcNow }));
app.MapGet("/ready", (Npgsql.NpgsqlDataSource dataSource, CancellationToken cancellationToken) =>
    MinimalApi.ReadyAsync(dataSource, cancellationToken));
app.MapGet("/metrics", (Npgsql.NpgsqlDataSource dataSource, CancellationToken cancellationToken) =>
    MinimalApi.MetricsAsync(serviceName, dataSource, cancellationToken));

app.Run();
