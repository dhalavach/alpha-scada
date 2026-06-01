using Alpha.Scada.ServiceDefaults;
using Alpha.Scada.Tenant.Application;
using Alpha.Scada.Tenant.Infrastructure;

const string serviceName = "alpha-scada-tenant";

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddServiceDatabase(builder.Configuration);
builder.Services.AddSingleton<TenantMigrator>();
builder.Services.AddSingleton<TenantRepository>();
builder.Services.AddSingleton<TenantService>();
builder.Services.AddJwtTokenService(builder.Configuration);

var app = builder.Build();
await app.Services.GetRequiredService<TenantMigrator>().MigrateAsync(CancellationToken.None);

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = serviceName, utc = DateTimeOffset.UtcNow }));
app.MapGet("/ready", (Npgsql.NpgsqlDataSource dataSource, CancellationToken cancellationToken) =>
    MinimalApi.ReadyAsync(dataSource, cancellationToken));
app.MapGet("/metrics", (Npgsql.NpgsqlDataSource dataSource, CancellationToken cancellationToken) =>
    MinimalApi.MetricsAsync(serviceName, dataSource, cancellationToken));

app.MapGet("/internal/v1/tenants", async (HttpContext context, JwtTokenService tokens, TenantService service) =>
{
    var user = HttpUserContext.FromBearerToken(context.Request.Headers, tokens);
    if (user is null)
    {
        return Results.Unauthorized();
    }

    var tenants = await service.GetTenantsAsync(user, context.RequestAborted);
    return Results.Ok(tenants);
});

app.MapGet("/internal/v1/tenants/resolve/{tenantKey}", async (string tenantKey, TenantService service, CancellationToken cancellationToken) =>
{
    var tenant = await service.ResolveAsync(tenantKey, cancellationToken);
    return tenant is null ? Results.NotFound() : Results.Ok(tenant);
});

app.MapGet("/internal/v1/tenants/{tenantId:guid}", async (Guid tenantId, TenantService service, CancellationToken cancellationToken) =>
{
    var tenant = await service.GetByIdAsync(tenantId, cancellationToken);
    return tenant is null ? Results.NotFound() : Results.Ok(tenant);
});

app.Run();
