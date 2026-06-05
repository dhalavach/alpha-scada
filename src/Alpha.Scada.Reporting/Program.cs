/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Reporting/Program.cs
- Module role: Alpha.Scada.Reporting is the reporting service. It orchestrates monthly report generation by combining report ontology, telemetry aggregates, and alarm counts.
- Local role: This is the composition root: it wires configuration, dependency injection, authentication, messaging, migrations, operational endpoints, and HTTP routes for one process.
- Architecture connection: this file participates in the service boundary. Follow the dependency direction from HTTP/message edges inward to application/domain code and outward to infrastructure.
- .NET/C# concepts to notice: Minimal APIs use route lambdas instead of controller classes; services are supplied through dependency injection parameters. Authentication/authorization are standard ASP.NET Core concepts: middleware validates tokens, then policies/claims decide access. async/await represents non-blocking I/O; it matters here because database, HTTP, NATS, and SignalR calls should not block server threads.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Reporting;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Reporting.Application;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Reporting.Infrastructure;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.ServiceDefaults;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.ServiceDefaults.Messaging;

// LEARN: declares a compile-time constant; callers cannot change this value.
const string serviceName = "alpha-scada-reporting";

// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
var builder = WebApplication.CreateBuilder(args);
// LEARN: registers a dependency with the built-in .NET dependency-injection container.
builder.Services.AddServiceDatabase(builder.Configuration);
// LEARN: registers a dependency with the built-in .NET dependency-injection container.
builder.Services.AddAlphaMigrator<ReportingMigrator>();
// LEARN: registers a dependency with the built-in .NET dependency-injection container.
builder.Services.AddSingleton<ReportingRepository>();
// LEARN: registers a dependency with the built-in .NET dependency-injection container.
builder.Services.AddSingleton<ReportingService>();
// LEARN: registers a dependency with the built-in .NET dependency-injection container.
builder.Services.AddAlphaJwtAuthentication(builder.Configuration);
// LEARN: registers a dependency with the built-in .NET dependency-injection container.
builder.Services.AddAlphaServiceClients(
// LEARN: continues an argument/object/collection initializer onto the next line.
    builder.Configuration,
// LEARN: continues an argument/object/collection initializer onto the next line.
    AlphaServiceClients.Asset,
// LEARN: continues an argument/object/collection initializer onto the next line.
    AlphaServiceClients.Telemetry,
// LEARN: continues an argument/object/collection initializer onto the next line.
    AlphaServiceClients.Alarm,
// LEARN: executes one C# statement; semicolons terminate most statements.
    AlphaServiceClients.TagCatalog);
// LEARN: enables the shared Wolverine/NATS messaging setup for this service.
builder.Host.UseAlphaMessaging(serviceName, MessagingTopology.Configure);

// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
var app = builder.Build();
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
await app.ApplyAlphaMigrationsAsync();
// LEARN: executes one C# statement; semicolons terminate most statements.
app.UseAlphaAuthorization();
// LEARN: executes one C# statement; semicolons terminate most statements.
app.MapAlphaOperationalEndpoints(serviceName);

// LEARN: attaches ASP.NET Core authorization so callers need an accepted authenticated user/policy.
var internalApi = app.MapGroup("/internal/v1").RequireAuthorization();

// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
internalApi.MapGet("/reports/monthly", async (AuthenticatedUser user, ReportingRepository repository, HttpContext context) =>
// LEARN: creates an ASP.NET Core HTTP response result.
    Results.Ok(await repository.GetMonthlyReportsAsync(user.Current, context.RequestAborted)));

// LEARN: executes one C# statement; semicolons terminate most statements.
app.Run();
