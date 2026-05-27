using System.Net.Http.Json;
using Alpha.Scada.Contracts;

namespace Alpha.Scada.Edge.Application;

public sealed class EdgeTelemetryPipeline(
    IConfiguration configuration,
    IHttpClientFactory httpClientFactory,
    ILogger<EdgeTelemetryPipeline> logger)
{
    public async Task IngestAsync(string tenantKey, string siteKey, string unitKey, EdgeTelemetryEnvelope envelope, CancellationToken cancellationToken)
    {
        if (envelope.SchemaVersion != "1.0" || envelope.UnitKey != unitKey)
        {
            logger.LogWarning("Rejected telemetry envelope for {Tenant}/{Site}/{Unit}.", tenantKey, siteKey, unitKey);
            return;
        }

        var tenant = await httpClientFactory.CreateClient("tenant")
            .GetFromJsonAsync<TenantDto>($"/internal/v1/tenants/resolve/{tenantKey}", cancellationToken);
        if (tenant is null)
        {
            throw new InvalidOperationException($"Tenant {tenantKey} is not allow-listed.");
        }

        var asset = httpClientFactory.CreateClient("asset");
        var resolvedUnit = await asset.GetFromJsonAsync<ResolvedUnitDto>(
            $"/internal/v1/units/resolve?tenantId={tenant.Id}&siteKey={siteKey}&unitKey={unitKey}",
            cancellationToken);
        if (resolvedUnit is null)
        {
            throw new InvalidOperationException($"Unit {tenantKey}/{siteKey}/{unitKey} is not allow-listed.");
        }

        var tagClient = httpClientFactory.CreateClient("tagCatalog");
        var tagResponse = await tagClient.PostAsJsonAsync(
            "/internal/v1/tags/resolve",
            new ResolveTagsRequest(tenant.Id, resolvedUnit.UnitId, envelope.Samples.Select(sample => sample.TagKey).Distinct().ToArray()),
            cancellationToken);
        tagResponse.EnsureSuccessStatusCode();
        var tags = await tagResponse.Content.ReadFromJsonAsync<IReadOnlyCollection<TagDto>>(cancellationToken) ?? [];
        var tagsByKey = tags.ToDictionary(tag => tag.Key);

        var resolvedSamples = envelope.Samples
            .Where(sample => tagsByKey.ContainsKey(sample.TagKey))
            .Select(sample =>
            {
                var tag = tagsByKey[sample.TagKey];
                return new ResolvedTelemetrySample(
                    tag.Id,
                    tag.Key,
                    tag.Name,
                    tag.Subsystem,
                    tag.EngineeringUnit,
                    tag.AlarmLow,
                    tag.AlarmHigh,
                    sample.Value,
                    sample.Quality,
                    sample.SourceTimestampUtc);
            })
            .ToArray();

        if (resolvedSamples.Length == 0)
        {
            logger.LogWarning("Telemetry envelope contained no known tag keys for {Unit}.", unitKey);
            return;
        }

        var telemetryResponse = await httpClientFactory.CreateClient("telemetry")
            .PostAsJsonAsync("/internal/v1/telemetry/ingest", new TelemetryIngestRequest(tenant.Id, resolvedUnit.UnitId, resolvedSamples), cancellationToken);
        telemetryResponse.EnsureSuccessStatusCode();

        await asset.PostAsync($"/internal/v1/units/{resolvedUnit.UnitId}/online", content: null, cancellationToken);

        var alarmResponse = await httpClientFactory.CreateClient("alarm")
            .PostAsJsonAsync("/internal/v1/alarms/evaluate", new AlarmEvaluationRequest(tenant.Id, resolvedUnit.UnitId, resolvedUnit.UnitName, resolvedSamples), cancellationToken);
        alarmResponse.EnsureSuccessStatusCode();

        var gateway = httpClientFactory.CreateClient("gateway");
        var serviceToken = configuration["ServiceAuth:Token"];
        var notification = new RealtimeNotificationRequest(tenant.Id, resolvedUnit.UnitId);
        await gateway.PostRealtimeAsync("/internal/v1/realtime/telemetry-updated", notification, serviceToken, cancellationToken);
        await gateway.PostRealtimeAsync("/internal/v1/realtime/alarms-changed", notification, serviceToken, cancellationToken);
        await gateway.PostRealtimeAsync("/internal/v1/realtime/unit-status-changed", notification, serviceToken, cancellationToken);
    }
}
