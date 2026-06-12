using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Alpha.Scada.ServiceDefaults;

public static class AlphaExceptionHandling
{
    public static IApplicationBuilder UseAlphaExceptionHandling(this IApplicationBuilder app)
    {
        app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
        {
            var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
            context.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger("Alpha.Scada.UnhandledException")
                .LogError(exception, "Unhandled request failure for {Method} {Path}.", context.Request.Method, context.Request.Path);

            await Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Unexpected error")
                .ExecuteAsync(context);
        }));
        return app;
    }
}
