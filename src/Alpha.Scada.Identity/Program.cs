using Alpha.Scada.Contracts;
using Alpha.Scada.Identity.Application;
using Alpha.Scada.Identity.Infrastructure;
using Alpha.Scada.ServiceDefaults;
using Npgsql;

const string serviceName = "alpha-scada-identity";

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddServiceDatabase(builder.Configuration);
builder.Services.AddSingleton<IdentityMigrator>();
builder.Services.AddSingleton<IdentityRepository>();
builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddSingleton<AuthService>();

var app = builder.Build();
await app.Services.GetRequiredService<IdentityMigrator>().MigrateAsync(CancellationToken.None);

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = serviceName, utc = DateTimeOffset.UtcNow }));
app.MapGet("/ready", MinimalApi.ReadyAsync);
app.MapGet("/metrics", () => MinimalApi.Metrics(serviceName));

app.MapPost("/internal/v1/auth/login", async (LoginRequest request, AuthService auth, CancellationToken cancellationToken) =>
{
    var response = await auth.LoginAsync(request, cancellationToken);
    return response is null ? Results.Unauthorized() : Results.Ok(response);
});

app.MapPost("/internal/v1/auth/logout", async (HttpContext context, JwtTokenService tokens, IdentityRepository repository) =>
{
    var user = HttpUserContext.FromBearerToken(context.Request.Headers, tokens);
    if (user is not null)
    {
        await repository.WriteAuditAsync(user.TenantId, user.UserId, "auth.logout", "User logged out", context.RequestAborted);
    }

    return Results.NoContent();
});

app.Run();
