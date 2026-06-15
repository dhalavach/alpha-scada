using Alpha.Scada.Edge.Application;
using Alpha.Scada.ServiceDefaults;

const string serviceName = "alpha-scada-edge";

var builder = WebApplication.CreateBuilder(args);
builder.AddAlphaObservability(serviceName);
builder.Services.AddProblemDetails();
builder.Services.AddServiceDatabase(builder.Configuration);
builder.Services.AddHostedService<ChpUnitSimulatorWorker>();

var app = builder.Build();
app.UseAlphaExceptionHandling();
app.MapAlphaOperationalEndpoints(serviceName);

app.Run();
