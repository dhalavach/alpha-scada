using Alpha.Scada.Edge.Application;
using Alpha.Scada.ServiceDefaults;

const string serviceName = "alpha-scada-edge";

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddServiceDatabase(builder.Configuration);
builder.Services.AddHostedService<ChpUnitSimulatorWorker>();

var app = builder.Build();
app.MapAlphaOperationalEndpoints(serviceName);

app.Run();
