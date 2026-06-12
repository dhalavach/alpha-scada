using System.Net;
using System.Net.Http.Json;
using Alpha.Scada.Contracts;
using Alpha.Scada.ServiceDefaults;
using Alpha.Scada.Telemetry.Application.Messaging;
using Microsoft.Extensions.Caching.Memory;

namespace Alpha.Scada.Telemetry.Application;

public sealed class CatalogCache(
    IHttpClientFactory httpClientFactory,
    IMemoryCache cache,
    TelemetryIngestionMetrics metrics)
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan NegativeCacheDuration = TimeSpan.FromMinutes(1);

    public async Task<ResolvedTelemetryBatch?> ResolveAsync(CanonicalTelemetry telemetry, CancellationToken cancellationToken)
    {
        var tenant = await ResolveTenantAsync(telemetry.TenantKey, cancellationToken);
        var unit = await ResolveUnitAsync(tenant.Id, telemetry.SiteKey, telemetry.UnitKey, cancellationToken);
        var tagKeys = telemetry.Readings.Select(sample => sample.TagKey).Distinct().ToArray();
        var tags = await ResolveTagsAsync(tenant.Id, unit.UnitId, tagKeys, cancellationToken);
        var tagsByKey = tags.ToDictionary(tag => tag.Key);
        var unknownTagCount = telemetry.Readings.Count(sample => !tagsByKey.ContainsKey(sample.TagKey));
        metrics.RecordUnknownTagsDropped(unknownTagCount);

        var samples = telemetry.Readings
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
                telemetry.TenantKey,
                telemetry.SiteKey,
                telemetry.UnitKey,
                unit.UnitName,
                samples);
    }

    private async Task<TenantDto> ResolveTenantAsync(string tenantKey, CancellationToken cancellationToken)
    {
        var cacheKey = $"tenant:{tenantKey}";
        if (cache.TryGetValue(cacheKey, out object? cached))
        {
            return cached switch
            {
                TenantDto cachedTenant => cachedTenant,
                NegativeResolution => throw new TelemetryResolutionException("tenant", tenantKey),
                _ => throw new InvalidOperationException($"Unexpected tenant cache entry for '{tenantKey}'.")
            };
        }

        var escapedTenantKey = Uri.EscapeDataString(tenantKey);
        using var response = await httpClientFactory.CreateClient(AlphaServiceClients.Tenant)
            .GetAsync($"/internal/v1/tenants/resolve/{escapedTenantKey}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            CacheNegative(cacheKey);
            throw new TelemetryResolutionException("tenant", tenantKey);
        }

        response.EnsureSuccessStatusCode();
        var tenant = await response.Content.ReadFromJsonAsync<TenantDto>(cancellationToken);
        if (tenant is null)
        {
            CacheNegative(cacheKey);
            throw new TelemetryResolutionException("tenant", tenantKey);
        }

        cache.Set(cacheKey, tenant, CacheDuration);
        return tenant;
    }

    private async Task<ResolvedUnitDto> ResolveUnitAsync(Guid tenantId, string siteKey, string unitKey, CancellationToken cancellationToken)
    {
        var cacheKey = $"unit:{tenantId}:{siteKey}:{unitKey}";
        if (cache.TryGetValue(cacheKey, out object? cached))
        {
            return cached switch
            {
                ResolvedUnitDto cachedUnit => cachedUnit,
                NegativeResolution => throw new TelemetryResolutionException("unit", $"{tenantId}/{siteKey}/{unitKey}"),
                _ => throw new InvalidOperationException($"Unexpected unit cache entry for '{cacheKey}'.")
            };
        }

        var escapedSiteKey = Uri.EscapeDataString(siteKey);
        var escapedUnitKey = Uri.EscapeDataString(unitKey);
        using var response = await httpClientFactory.CreateClient(AlphaServiceClients.Asset)
            .GetAsync(
                $"/internal/v1/units/resolve?tenantId={tenantId}&siteKey={escapedSiteKey}&unitKey={escapedUnitKey}",
                cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            CacheNegative(cacheKey);
            throw new TelemetryResolutionException("unit", $"{tenantId}/{siteKey}/{unitKey}");
        }

        response.EnsureSuccessStatusCode();
        var unit = await response.Content.ReadFromJsonAsync<ResolvedUnitDto>(cancellationToken);
        if (unit is null)
        {
            CacheNegative(cacheKey);
            throw new TelemetryResolutionException("unit", $"{tenantId}/{siteKey}/{unitKey}");
        }

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
        if (cache.TryGetValue(cacheKey, out object? cached))
        {
            return cached switch
            {
                IReadOnlyCollection<TagDto> cachedTags => cachedTags,
                NegativeResolution => throw new TelemetryResolutionException("tag set", string.Join(',', tagKeys)),
                _ => throw new InvalidOperationException($"Unexpected tag cache entry for '{cacheKey}'.")
            };
        }

        using var response = await httpClientFactory.CreateClient(AlphaServiceClients.TagCatalog)
            .PostAsJsonAsync("/internal/v1/tags/resolve", new ResolveTagsRequest(tenantId, unitId, tagKeys), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            CacheNegative(cacheKey);
            throw new TelemetryResolutionException("tag set", string.Join(',', tagKeys));
        }

        response.EnsureSuccessStatusCode();
        var tags = await response.Content.ReadFromJsonAsync<IReadOnlyCollection<TagDto>>(cancellationToken) ?? [];

        cache.Set(cacheKey, tags, CacheDuration);
        return tags;
    }

    private void CacheNegative(string cacheKey) =>
        cache.Set(cacheKey, NegativeResolution.Instance, NegativeCacheDuration);

    private sealed class NegativeResolution
    {
        public static NegativeResolution Instance { get; } = new();
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
