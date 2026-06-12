using Alpha.Scada.Reporting;
using Alpha.Scada.Reporting.Application;
using Alpha.Scada.Reporting.Infrastructure;
using Alpha.Scada.ServiceDefaults;
using Alpha.Scada.ServiceDefaults.Messaging;

const string serviceName = "alpha-scada-reporting";

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddProblemDetails();
builder.Services.AddServiceDatabase(builder.Configuration);
builder.Services.AddAlphaMigrator<ReportingMigrator>();
builder.Services.AddSingleton<ReportingRepository>();
builder.Services.AddSingleton<ReportingService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddAlphaJwtAuthentication(builder.Configuration);
builder.Services.AddAlphaServiceClients(
    builder.Configuration,
    AlphaServiceClients.Asset,
    AlphaServiceClients.Telemetry,
    AlphaServiceClients.Alarm,
    AlphaServiceClients.TagCatalog);
builder.Host.UseAlphaMessaging(serviceName, MessagingTopology.Configure);

var app = builder.Build();
await app.ApplyAlphaMigrationsAsync();
app.UseAlphaExceptionHandling();
app.UseAlphaAuthorization();
app.MapAlphaOperationalEndpoints(serviceName);

var internalApi = app.MapGroup("/internal/v1").RequireAuthorization();

internalApi.MapGet("/reports/monthly", async (AuthenticatedUser user, ReportingRepository repository, CancellationToken cancellationToken) =>
    Results.Ok(await repository.GetMonthlyReportsAsync(user.Current, cancellationToken)));

app.Run();
