using Alpha.Scada.Contracts;
using Alpha.Scada.Reporting.Application;
using Alpha.Scada.Reporting.Contracts;
using Alpha.Scada.Reporting.Infrastructure;
using Alpha.Scada.ServiceDefaults;
using Alpha.Scada.ServiceDefaults.Messaging;
using Wolverine.Postgresql;

const string serviceName = "alpha-scada-reporting";

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddServiceDatabase(builder.Configuration);
builder.Services.AddSingleton<ReportingMigrator>();
builder.Services.AddSingleton<ReportingRepository>();
builder.Services.AddSingleton<ReportingService>();
builder.Services.AddJwtTokenService(builder.Configuration);
builder.Services.AddHttpClient("asset", client => client.BaseAddress = new Uri(builder.Configuration["Services:Asset"] ?? "http://localhost:5212"));
builder.Services.AddHttpClient("telemetry", client => client.BaseAddress = new Uri(builder.Configuration["Services:Telemetry"] ?? "http://localhost:5214"));
builder.Services.AddHttpClient("alarm", client => client.BaseAddress = new Uri(builder.Configuration["Services:Alarm"] ?? "http://localhost:5215"));
builder.Host.UseAlphaMessaging("reporting", options =>
{
    options.ListenToPostgresqlQueue("reports_requested");
    options.PublishMessage<ReportCompleted>().ToPostgresqlQueue("reports_completed");
});

var app = builder.Build();
await app.Services.GetRequiredService<ReportingMigrator>().MigrateAsync(CancellationToken.None);

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = serviceName, utc = DateTimeOffset.UtcNow }));
app.MapGet("/ready", (Npgsql.NpgsqlDataSource dataSource, CancellationToken cancellationToken) =>
    MinimalApi.ReadyAsync(dataSource, cancellationToken));
app.MapGet("/metrics", (Npgsql.NpgsqlDataSource dataSource, CancellationToken cancellationToken) =>
    MinimalApi.MetricsAsync(serviceName, dataSource, cancellationToken));

app.MapGet("/internal/v1/reports/monthly", async (HttpContext context, JwtTokenService tokens, ReportingRepository repository) =>
{
    var user = HttpUserContext.FromBearerToken(context.Request.Headers, tokens);
    return user is null ? Results.Unauthorized() : Results.Ok(await repository.GetMonthlyReportsAsync(user, context.RequestAborted));
});

app.Run();
