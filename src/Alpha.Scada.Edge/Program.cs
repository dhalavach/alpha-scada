/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Edge/Program.cs
- Module role: Alpha.Scada.Edge is the edge/simulator service. In this codebase it stands in for field-side publishers by producing raw telemetry onto NATS subjects.
- Local role: This is the composition root: it wires configuration, dependency injection, authentication, messaging, migrations, operational endpoints, and HTTP routes for one process.
- Architecture connection: this file participates in the service boundary. Follow the dependency direction from HTTP/message edges inward to application/domain code and outward to infrastructure.
- .NET/C# concepts to notice: Minimal APIs use route lambdas instead of controller classes; services are supplied through dependency injection parameters.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

using Alpha.Scada.Edge.Application;
using Alpha.Scada.Edge.Infrastructure;
using Alpha.Scada.ServiceDefaults;

const string serviceName = "alpha-scada-edge";

var builder = WebApplication.CreateBuilder(args);
// LEARN: registers a dependency with the built-in .NET dependency-injection container.
builder.Services.AddServiceDatabase(builder.Configuration);
// LEARN: registers a dependency with the built-in .NET dependency-injection container.
builder.Services.AddAlphaMigrator<EdgeMigrator>();
// LEARN: registers a dependency with the built-in .NET dependency-injection container.
builder.Services.AddHostedService<ChpUnitSimulatorWorker>();

var app = builder.Build();
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
await app.ApplyAlphaMigrationsAsync();
app.MapAlphaOperationalEndpoints(serviceName);

app.Run();
