/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Telemetry/Application/CatalogCache.cs
- Module role: Alpha.Scada.Telemetry is the historian and normalization boundary. It converts raw edge payloads into canonical readings, resolves tags, persists current/history rows, and emits domain events after storage.
- Local role: This file is a lookup/caching helper. It keeps hot-path code from repeating service calls while preserving the authoritative owner of the data.
- Architecture connection: application files orchestrate use cases and message handling; they may call repositories/clients but should keep transport details at the boundary.
- .NET/C# concepts to notice: IHttpClientFactory centralizes outbound HTTP clients so service-to-service calls get shared base addresses and resilience policy. IMemoryCache is process-local caching; it reduces repeated lookups but is not a source of truth. C# records are concise immutable data carriers with value-based equality, useful for DTOs and domain events. async/await represents non-blocking I/O; it matters here because database, HTTP, NATS, and SignalR calls should not block server threads.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

using System.Net.Http.Json;
using Alpha.Scada.Contracts;
using Alpha.Scada.ServiceDefaults;
using Alpha.Scada.Telemetry.Application.Messaging;
using Microsoft.Extensions.Caching.Memory;

namespace Alpha.Scada.Telemetry.Application;

public sealed class CatalogCache(IHttpClientFactory httpClientFactory, IMemoryCache cache)
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(1);

    public async Task<ResolvedTelemetryBatch?> ResolveAsync(CanonicalTelemetry telemetry, CancellationToken cancellationToken)
    {
        var tenant = await ResolveTenantAsync(telemetry.TenantKey, cancellationToken);
        var unit = await ResolveUnitAsync(tenant.Id, telemetry.SiteKey, telemetry.UnitKey, cancellationToken);
        var tagKeys = telemetry.Readings.Select(sample => sample.TagKey).Distinct().ToArray();
        var tags = await ResolveTagsAsync(tenant.Id, unit.UnitId, tagKeys, cancellationToken);
        var tagsByKey = tags.ToDictionary(tag => tag.Key);

        // TODO: Unknown tags are currently dropped. Follow-up: quarantine or auto-provision them.
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
        if (cache.TryGetValue(cacheKey, out TenantDto? cached) && cached is not null)
        {
            return cached;
        }

        var tenant = await httpClientFactory.CreateClient(AlphaServiceClients.Tenant)
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

        var unit = await httpClientFactory.CreateClient(AlphaServiceClients.Asset)
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

        var response = await httpClientFactory.CreateClient(AlphaServiceClients.TagCatalog)
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
