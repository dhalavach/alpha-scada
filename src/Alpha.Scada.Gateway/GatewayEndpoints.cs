using System.Net;
using System.Net.Http.Json;
using Alpha.Scada.Contracts;
using Alpha.Scada.Gateway.Application;
using Alpha.Scada.Reporting.Contracts;
using Alpha.Scada.ServiceDefaults;
using Alpha.Scada.ServiceDefaults.Messaging;
using Wolverine;

namespace Alpha.Scada.Gateway;

public static class GatewayEndpoints
{
    public static WebApplication MapGatewayEndpoints(this WebApplication app)
    {
        app.MapPost("/api/auth/login", LoginAsync);

        var api = app.MapGroup("/api").RequireAuthorization();

        api.MapPost("/auth/logout", LogoutAsync);
        api.MapGet("/me", (AuthenticatedUser user) => Results.Ok(user.Current));
        api.MapGet("/tenants", (IHttpClientFactory factory, HttpContext context) =>
            ForwardGetAsync<IReadOnlyCollection<TenantDto>>(
                factory.CreateClient(AlphaServiceClients.Tenant),
                "/internal/v1/tenants",
                context,
                context.RequestAborted));
        api.MapGet("/sites", (IHttpClientFactory factory, HttpContext context) =>
            ForwardGetAsync<IReadOnlyCollection<SiteDto>>(
                factory.CreateClient(AlphaServiceClients.Asset),
                "/internal/v1/sites",
                context,
                context.RequestAborted));
        api.MapGet("/sites/{siteId:guid}/units", (Guid siteId, IHttpClientFactory factory, HttpContext context) =>
            ForwardGetAsync<IReadOnlyCollection<UnitDto>>(
                factory.CreateClient(AlphaServiceClients.Asset),
                $"/internal/v1/sites/{siteId}/units",
                context,
                context.RequestAborted));
        api.MapGet("/units/{unitId:guid}", (Guid unitId, IHttpClientFactory factory, HttpContext context) =>
            ForwardGetAsync<UnitDto>(
                factory.CreateClient(AlphaServiceClients.Asset),
                $"/internal/v1/units/{unitId}",
                context,
                context.RequestAborted));
        api.MapGet("/units/{unitId:guid}/tags/current", GetCurrentTagsAsync);
        api.MapGet("/tags/{tagId:guid}/history", GetHistoryAsync);
        api.MapGet("/alarms/active", (IHttpClientFactory factory, HttpContext context) =>
            ForwardGetAsync<IReadOnlyCollection<AlarmDto>>(
                factory.CreateClient(AlphaServiceClients.Alarm),
                "/internal/v1/alarms/active",
                context,
                context.RequestAborted));
        api.MapPost("/alarms/{alarmId:guid}/ack", AcknowledgeAlarmAsync);
        api.MapGet("/reports/monthly", (IHttpClientFactory factory, HttpContext context) =>
            ForwardGetAsync<IReadOnlyCollection<MonthlyReportDto>>(
                factory.CreateClient(AlphaServiceClients.Reporting),
                "/internal/v1/reports/monthly",
                context,
                context.RequestAborted));
        api.MapPost("/reports/monthly/run", RunMonthlyReportAsync);

        return app;
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        IHttpClientFactory factory,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("Alpha.Scada.Gateway.Login");
        try
        {
            using var response = await factory.CreateClient(AlphaServiceClients.Identity)
                .PostAsJsonAsync("/internal/v1/auth/login", request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return Results.Ok(await response.Content.ReadFromJsonAsync<LoginResponse>(cancellationToken));
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                return Results.Unauthorized();
            }

            logger.LogWarning("Identity login request failed with downstream status {StatusCode}.", (int)response.StatusCode);
            return Results.StatusCode(StatusCodes.Status502BadGateway);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Identity service was unavailable during login.");
            return Results.StatusCode(StatusCodes.Status502BadGateway);
        }
    }

    private static async Task<IResult> LogoutAsync(HttpContext context, IHttpClientFactory factory)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/internal/v1/auth/logout").WithBearerToken(context);
        using var response = await factory.CreateClient(AlphaServiceClients.Identity).SendAsync(request, context.RequestAborted);
        return Results.NoContent();
    }

    private static async Task<IResult> GetCurrentTagsAsync(Guid unitId, HttpContext context, IHttpClientFactory factory)
    {
        using var tagRequest = new HttpRequestMessage(HttpMethod.Get, $"/internal/v1/units/{unitId}/tags").WithBearerToken(context);
        using var currentRequest = new HttpRequestMessage(HttpMethod.Get, $"/internal/v1/telemetry/units/{unitId}/current").WithBearerToken(context);
        var tagTask = factory.CreateClient(AlphaServiceClients.TagCatalog).SendAsync(tagRequest, context.RequestAborted);
        var currentTask = factory.CreateClient(AlphaServiceClients.Telemetry).SendAsync(currentRequest, context.RequestAborted);
        await Task.WhenAll(tagTask, currentTask);
        using var tagResponse = await tagTask;
        using var currentResponse = await currentTask;
        if (!tagResponse.IsSuccessStatusCode || !currentResponse.IsSuccessStatusCode)
        {
            return Results.StatusCode(StatusCodes.Status502BadGateway);
        }

        var tags = await tagResponse.Content.ReadFromJsonAsync<IReadOnlyCollection<TagDto>>(context.RequestAborted) ?? [];
        var values = await currentResponse.Content.ReadFromJsonAsync<IReadOnlyCollection<TagValueDto>>(context.RequestAborted) ?? [];
        var valueByTag = values.ToDictionary(value => value.TagId);
        var result = tags.Select(tag =>
        {
            valueByTag.TryGetValue(tag.Id, out var current);
            return new TagCurrentDto(
                tag.Id,
                tag.TenantId,
                tag.UnitId,
                tag.Key,
                tag.Name,
                tag.Subsystem,
                current?.Value,
                tag.EngineeringUnit,
                current?.Quality ?? "stale",
                current?.TimestampUtc);
        }).ToArray();
        return Results.Ok(result);
    }

    private static Task<IResult> GetHistoryAsync(
        Guid tagId,
        int? minutes,
        IHttpClientFactory factory,
        HttpContext context)
    {
        var window = minutes ?? 30;
        if (window is < 1 or > 24 * 60)
        {
            return Task.FromResult(Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid history window",
                detail: "minutes must be between 1 and 1440."));
        }

        return ForwardGetAsync<IReadOnlyCollection<TelemetryHistoryPointDto>>(
            factory.CreateClient(AlphaServiceClients.Telemetry),
            $"/internal/v1/telemetry/tags/{tagId}/history?minutes={window}",
            context,
            context.RequestAborted);
    }

    private static async Task<IResult> AcknowledgeAlarmAsync(
        Guid alarmId,
        HttpContext context,
        IHttpClientFactory factory)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/internal/v1/alarms/{alarmId}/ack").WithBearerToken(context);
        using var response = await factory.CreateClient(AlphaServiceClients.Alarm).SendAsync(request, context.RequestAborted);
        return Results.StatusCode((int)response.StatusCode);
    }

    private static async Task<IResult> RunMonthlyReportAsync(
        ReportRunRequest request,
        AuthenticatedUser user,
        HttpContext context,
        IHttpClientFactory factory,
        IMessageBus bus)
    {
        if (!RoleRules.CanRunReports(user.Current.Role))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var period = string.IsNullOrWhiteSpace(request.Period)
            ? DateTimeOffset.UtcNow.ToString("yyyy-MM")
            : request.Period;
        if (!MonthPeriod.TryParse(period, out _))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid report period",
                detail: "period must use yyyy-MM format.");
        }

        using var unitRequest = new HttpRequestMessage(HttpMethod.Get, $"/internal/v1/units/{request.UnitId}").WithBearerToken(context);
        using var unitResponse = await factory.CreateClient(AlphaServiceClients.Asset).SendAsync(unitRequest, context.RequestAborted);
        if (!unitResponse.IsSuccessStatusCode)
        {
            return Results.StatusCode((int)unitResponse.StatusCode);
        }

        var unit = await unitResponse.Content.ReadFromJsonAsync<UnitDto>(context.RequestAborted);
        if (unit is null)
        {
            return Results.StatusCode(StatusCodes.Status502BadGateway);
        }

        var jobId = Guid.NewGuid();
        await bus.PublishAsync(new ReportRequested(
            jobId,
            unit.TenantId,
            unit.Id,
            period,
            user.Current.UserId,
            DateTimeOffset.UtcNow,
            ServiceIdentity.CurrentCorrelationId()));

        return Results.Accepted($"/api/reports/monthly?jobId={jobId}", new { jobId, status = "queued" });
    }

    private static async Task<IResult> ForwardGetAsync<T>(
        HttpClient client,
        string path,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path).WithBearerToken(context);
        using var response = await client.SendAsync(request, cancellationToken);
        return response.IsSuccessStatusCode
            ? Results.Ok(await response.Content.ReadFromJsonAsync<T>(cancellationToken))
            : Results.StatusCode((int)response.StatusCode);
    }
}
