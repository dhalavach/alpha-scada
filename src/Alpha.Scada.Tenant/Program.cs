using Alpha.Scada.ServiceDefaults;
using Alpha.Scada.Tenant.Application;
using Alpha.Scada.Tenant.Infrastructure;

const string serviceName = "alpha-scada-tenant";

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddServiceDatabase(builder.Configuration);
builder.Services.AddAlphaMigrator<TenantMigrator>();
builder.Services.AddSingleton<TenantRepository>();
builder.Services.AddSingleton<TenantService>();
builder.Services.AddAlphaJwtAuthentication(builder.Configuration);

var app = builder.Build();
await app.ApplyAlphaMigrationsAsync();
app.UseAlphaAuthorization();
app.MapAlphaOperationalEndpoints(serviceName);

app.MapGroup("/internal/v1")
    .RequireAuthorization()
    .MapGet("/tenants", async (AuthenticatedUser user, TenantService service, HttpContext context) =>
        Results.Ok(await service.GetTenantsAsync(user.Current, context.RequestAborted)));

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
