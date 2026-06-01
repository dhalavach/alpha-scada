using Alpha.Scada.Contracts;
using Alpha.Scada.ServiceDefaults;
using Alpha.Scada.TagCatalog.Application;
using Alpha.Scada.TagCatalog.Infrastructure;

const string serviceName = "alpha-scada-tag-catalog";

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddServiceDatabase(builder.Configuration);
builder.Services.AddSingleton<TagCatalogMigrator>();
builder.Services.AddSingleton<TagCatalogRepository>();
builder.Services.AddSingleton<TagCatalogService>();
builder.Services.AddJwtTokenService(builder.Configuration);

var app = builder.Build();
await app.Services.GetRequiredService<TagCatalogMigrator>().MigrateAsync(CancellationToken.None);

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = serviceName, utc = DateTimeOffset.UtcNow }));
app.MapGet("/ready", (Npgsql.NpgsqlDataSource dataSource, CancellationToken cancellationToken) =>
    MinimalApi.ReadyAsync(dataSource, cancellationToken));
app.MapGet("/metrics", (Npgsql.NpgsqlDataSource dataSource, CancellationToken cancellationToken) =>
    MinimalApi.MetricsAsync(serviceName, dataSource, cancellationToken));

app.MapGet("/internal/v1/units/{unitId:guid}/tags", async (Guid unitId, HttpContext context, JwtTokenService tokens, TagCatalogService service) =>
{
    var user = HttpUserContext.FromBearerToken(context.Request.Headers, tokens);
    return user is null ? Results.Unauthorized() : Results.Ok(await service.GetTagsForUnitAsync(unitId, user, context.RequestAborted));
});

app.MapPost("/internal/v1/tags/resolve", async (ResolveTagsRequest request, TagCatalogService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.ResolveTagsAsync(request, cancellationToken)));

app.Run();
