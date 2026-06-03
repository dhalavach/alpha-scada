using Alpha.Scada.Reporting;
using Alpha.Scada.Reporting.Application;
using Alpha.Scada.Reporting.Infrastructure;
using Alpha.Scada.ServiceDefaults;
using Alpha.Scada.ServiceDefaults.Messaging;

const string serviceName = "alpha-scada-reporting";

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddServiceDatabase(builder.Configuration);
builder.Services.AddAlphaMigrator<ReportingMigrator>();
builder.Services.AddSingleton<ReportingRepository>();
builder.Services.AddSingleton<ReportingService>();
builder.Services.AddAlphaJwtAuthentication(builder.Configuration);
builder.Services.AddHttpClient("asset", client => client.BaseAddress = new Uri(builder.Configuration["Services:Asset"] ?? "http://localhost:5212")).AddAlphaResilience();
builder.Services.AddHttpClient("telemetry", client => client.BaseAddress = new Uri(builder.Configuration["Services:Telemetry"] ?? "http://localhost:5214")).AddAlphaResilience();
builder.Services.AddHttpClient("alarm", client => client.BaseAddress = new Uri(builder.Configuration["Services:Alarm"] ?? "http://localhost:5215")).AddAlphaResilience();
builder.Services.AddHttpClient("tagCatalog", client => client.BaseAddress = new Uri(builder.Configuration["Services:TagCatalog"] ?? "http://localhost:5213")).AddAlphaResilience();
builder.Host.UseAlphaMessaging(serviceName, MessagingTopology.Configure);

var app = builder.Build();
await app.ApplyAlphaMigrationsAsync();
app.UseAlphaAuthorization();
app.MapAlphaOperationalEndpoints(serviceName);

var internalApi = app.MapGroup("/internal/v1").RequireAuthorization();

internalApi.MapGet("/reports/monthly", async (AuthenticatedUser user, ReportingRepository repository, HttpContext context) =>
    Results.Ok(await repository.GetMonthlyReportsAsync(user.Current, context.RequestAborted)));

app.Run();
