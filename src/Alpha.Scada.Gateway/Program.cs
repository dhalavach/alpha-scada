using Alpha.Scada.Gateway;
using Alpha.Scada.Gateway.Realtime;
using Alpha.Scada.ServiceDefaults;
using Alpha.Scada.ServiceDefaults.Messaging;
using Microsoft.AspNetCore.Authentication.JwtBearer;

const string serviceName = "alpha-scada-gateway";

var builder = WebApplication.CreateBuilder(args);
builder.AddAlphaObservability(serviceName);
builder.Services.AddProblemDetails();
builder.Services.AddGatewayLoginRateLimiting();
builder.Services.AddServiceDatabase(builder.Configuration);
builder.Services.AddAlphaJwtAuthentication(builder.Configuration, options =>
{
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            if (context.HttpContext.Request.Path.StartsWithSegments("/hubs/telemetry")
                && context.Request.Query.TryGetValue("access_token", out var accessToken))
            {
                context.Token = accessToken.ToString();
            }

            return Task.CompletedTask;
        }
    };
});
builder.Services.AddSignalR();
builder.Services.AddCors(options =>
{
    var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
        ?? ["http://localhost:5173", "http://localhost:8080"];
    options.AddDefaultPolicy(policy => policy
        .WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod());
});
builder.Services.AddAlphaServiceClients(
    builder.Configuration,
    AlphaServiceClients.Identity,
    AlphaServiceClients.Tenant,
    AlphaServiceClients.Asset,
    AlphaServiceClients.TagCatalog,
    AlphaServiceClients.Telemetry,
    AlphaServiceClients.Alarm,
    AlphaServiceClients.Reporting);
builder.Host.UseAlphaMessaging(serviceName, MessagingTopology.Configure);

var app = builder.Build();
app.UseForwardedHeaders();
app.UseAlphaExceptionHandling();
app.UseRateLimiter();
app.UseCors();
app.UseAlphaAuthorization();
app.MapAlphaOperationalEndpoints(serviceName);
app.MapGatewayEndpoints();

app.MapHub<TelemetryHub>("/hubs/telemetry").RequireAuthorization();

app.Run();
