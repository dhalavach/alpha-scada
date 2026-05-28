using System.Net.Http.Json;
using Alpha.Scada.Contracts;
using Alpha.Scada.Contracts.Messaging;
using Alpha.Scada.Telemetry.Application.Messaging;
using Microsoft.Extensions.Caching.Memory;

namespace Alpha.Scada.Telemetry.Application;

public sealed class CatalogCache(IHttpClientFactory httpClientFactory, IMemoryCache cache)
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(1);

    public async Task<ResolvedTelemetryBatch?> ResolveAsync(
        TelemetryTopic topic,
        TelemetryEnvelopeV1 envelope,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(envelope.UnitKey, topic.UnitKey, StringComparison.Ordinal))
        {
            return null;
        }

        var tenant = await ResolveTenantAsync(topic.TenantKey, cancellationToken);
        var unit = await ResolveUnitAsync(tenant.Id, topic.SiteKey, topic.UnitKey, cancellationToken);
        var tagKeys = envelope.Samples.Select(sample => sample.TagKey).Distinct().ToArray();
        var tags = await ResolveTagsAsync(tenant.Id, unit.UnitId, tagKeys, cancellationToken);
        var tagsByKey = tags.ToDictionary(tag => tag.Key);

        var samples = envelope.Samples
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

        return samples.Length == 0
            ? null
            : new ResolvedTelemetryBatch(
                tenant.Id,
                unit.SiteId,
                unit.UnitId,
                topic.TenantKey,
                topic.SiteKey,
                topic.UnitKey,
                unit.UnitName,
                samples);
    }

    private async Task<TenantDto> ResolveTenantAsync(string tenantKey, CancellationToken cancellationToken)
    {
        var cacheKey = $"tenant:{tenantKey}";
        if (cache.TryGetValue(cacheKey, out TenantDto? cached) && cached is not null)
        {
            return cached;
        }

        var tenant = await httpClientFactory.CreateClient("tenant")
            .GetFromJsonAsync<TenantDto>($"/internal/v1/tenants/resolve/{tenantKey}", cancellationToken)
            ?? throw new InvalidOperationException($"Tenant {tenantKey} is not allow-listed.");

        cache.Set(cacheKey, tenant, CacheDuration);
        return tenant;
    }

    private async Task<ResolvedUnitDto> ResolveUnitAsync(Guid tenantId, string siteKey, string unitKey, CancellationToken cancellationToken)
    {
        var cacheKey = $"unit:{tenantId}:{siteKey}:{unitKey}";
        if (cache.TryGetValue(cacheKey, out ResolvedUnitDto? cached) && cached is not null)
        {
            return cached;
        }

        var unit = await httpClientFactory.CreateClient("asset")
            .GetFromJsonAsync<ResolvedUnitDto>(
                $"/internal/v1/units/resolve?tenantId={tenantId}&siteKey={siteKey}&unitKey={unitKey}",
                cancellationToken)
            ?? throw new InvalidOperationException($"Unit {tenantId}/{siteKey}/{unitKey} is not allow-listed.");

        cache.Set(cacheKey, unit, CacheDuration);
        return unit;
    }

    private async Task<IReadOnlyCollection<TagDto>> ResolveTagsAsync(
        Guid tenantId,
        Guid unitId,
        IReadOnlyCollection<string> tagKeys,
        CancellationToken cancellationToken)
    {
        var cacheKey = $"tags:{tenantId}:{unitId}:{string.Join('|', tagKeys.Order())}";
        if (cache.TryGetValue(cacheKey, out IReadOnlyCollection<TagDto>? cached) && cached is not null)
        {
            return cached;
        }

        var response = await httpClientFactory.CreateClient("tagCatalog")
            .PostAsJsonAsync("/internal/v1/tags/resolve", new ResolveTagsRequest(tenantId, unitId, tagKeys), cancellationToken);
        response.EnsureSuccessStatusCode();
        var tags = await response.Content.ReadFromJsonAsync<IReadOnlyCollection<TagDto>>(cancellationToken) ?? [];

        cache.Set(cacheKey, tags, CacheDuration);
        return tags;
    }
}

public sealed record ResolvedTelemetryBatch(
    Guid TenantId,
    Guid SiteId,
    Guid UnitId,
    string TenantKey,
    string SiteKey,
    string UnitKey,
    string UnitName,
    IReadOnlyCollection<ResolvedTelemetrySample> Samples);
