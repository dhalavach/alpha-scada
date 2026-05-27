using Alpha.Scada.ServiceDefaults;
using Alpha.Scada.Tenant.Application;
using Alpha.Scada.Tenant.Infrastructure;

const string serviceName = "alpha-scada-tenant";

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddServiceDatabase(builder.Configuration);
builder.Services.AddSingleton<TenantMigrator>();
builder.Services.AddSingleton<TenantRepository>();
builder.Services.AddSingleton<TenantService>();

var app = builder.Build();
await app.Services.GetRequiredService<TenantMigrator>().MigrateAsync(CancellationToken.None);

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = serviceName, utc = DateTimeOffset.UtcNow }));
app.MapGet("/ready", MinimalApi.ReadyAsync);
app.MapGet("/metrics", () => MinimalApi.Metrics(serviceName));

app.MapGet("/internal/v1/tenants", async (HttpContext context, TenantService service) =>
{
    var user = HttpUserContext.FromHeaders(context.Request.Headers);
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

app.Run();
