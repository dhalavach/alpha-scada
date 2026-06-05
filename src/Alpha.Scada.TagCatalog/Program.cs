/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.TagCatalog/Program.cs
- Module role: Alpha.Scada.TagCatalog is the tag-catalog service. It owns tag definitions, engineering units, thresholds, subsystem grouping, and report ontology/configuration rather than scattering those constants through code.
- Local role: This is the composition root: it wires configuration, dependency injection, authentication, messaging, migrations, operational endpoints, and HTTP routes for one process.
- Architecture connection: this file participates in the service boundary. Follow the dependency direction from HTTP/message edges inward to application/domain code and outward to infrastructure.
- .NET/C# concepts to notice: Minimal APIs use route lambdas instead of controller classes; services are supplied through dependency injection parameters. Authentication/authorization are standard ASP.NET Core concepts: middleware validates tokens, then policies/claims decide access. async/await represents non-blocking I/O; it matters here because database, HTTP, NATS, and SignalR calls should not block server threads.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

using Alpha.Scada.Contracts;
using Alpha.Scada.ServiceDefaults;
using Alpha.Scada.TagCatalog.Application;
using Alpha.Scada.TagCatalog.Infrastructure;

const string serviceName = "alpha-scada-tag-catalog";

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddServiceDatabase(builder.Configuration);
builder.Services.AddAlphaMigrator<TagCatalogMigrator>();
builder.Services.AddSingleton<TagCatalogRepository>();
builder.Services.AddSingleton<TagCatalogService>();
builder.Services.AddAlphaJwtAuthentication(builder.Configuration);

var app = builder.Build();
await app.ApplyAlphaMigrationsAsync();
app.UseAlphaAuthorization();
app.MapAlphaOperationalEndpoints(serviceName);

app.MapGroup("/internal/v1")
    .RequireAuthorization()
    .MapGet("/units/{unitId:guid}/tags", async (Guid unitId, AuthenticatedUser user, TagCatalogService service, HttpContext context) =>
        Results.Ok(await service.GetTagsForUnitAsync(unitId, user.Current, context.RequestAborted)));

app.MapPost("/internal/v1/tags/resolve", async (ResolveTagsRequest request, TagCatalogService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.ResolveTagsAsync(request, cancellationToken)));

app.MapGet("/internal/v1/report-config/units/{unitId:guid}", async (Guid unitId, Guid tenantId, TagCatalogService service, CancellationToken cancellationToken) =>
{
    var profile = await service.GetReportProfileAsync(tenantId, unitId, cancellationToken);
    return profile is null ? Results.NotFound() : Results.Ok(profile);
});

app.Run();
