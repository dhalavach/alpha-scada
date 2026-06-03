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
