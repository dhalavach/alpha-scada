using Alpha.Scada.Contracts;
using Alpha.Scada.Identity.Application;
using Alpha.Scada.Identity.Infrastructure;
using Alpha.Scada.ServiceDefaults;

const string serviceName = "alpha-scada-identity";

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddServiceDatabase(builder.Configuration);
builder.Services.AddAlphaMigrator<IdentityMigrator>();
builder.Services.AddSingleton<IdentityRepository>();
builder.Services.AddAlphaJwtAuthentication(builder.Configuration);
builder.Services.AddSingleton<AuthService>();

var app = builder.Build();
await app.ApplyAlphaMigrationsAsync();
app.UseAlphaAuthorization();
app.MapAlphaOperationalEndpoints(serviceName);

app.MapPost("/internal/v1/auth/login", async (LoginRequest request, AuthService auth, CancellationToken cancellationToken) =>
{
    var response = await auth.LoginAsync(request, cancellationToken);
    return response is null ? Results.Unauthorized() : Results.Ok(response);
});

app.MapGroup("/internal/v1")
    .RequireAuthorization()
    .MapPost("/auth/logout", async (AuthenticatedUser user, IdentityRepository repository, CancellationToken cancellationToken) =>
    {
        await repository.WriteAuditAsync(
            user.Current.TenantId,
            user.Current.UserId,
            "auth.logout",
            "User logged out",
            cancellationToken);
        return Results.NoContent();
    });

app.Run();
