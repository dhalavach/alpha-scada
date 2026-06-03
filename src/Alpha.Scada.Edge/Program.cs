using Alpha.Scada.Edge.Application;
using Alpha.Scada.Edge.Infrastructure;
using Alpha.Scada.ServiceDefaults;

const string serviceName = "alpha-scada-edge";

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddServiceDatabase(builder.Configuration);
builder.Services.AddAlphaMigrator<EdgeMigrator>();
builder.Services.AddHostedService<ChpUnitSimulatorWorker>();

var app = builder.Build();
await app.ApplyAlphaMigrationsAsync();
app.MapAlphaOperationalEndpoints(serviceName);

app.Run();
