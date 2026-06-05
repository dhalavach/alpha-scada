/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Alarm/Application/ThresholdCache.cs
- Module role: Alpha.Scada.Alarm is the alarm service. It evaluates domain telemetry/status events against thresholds and quality rules, persists alarm lifecycle state, and publishes alarm changes.
- Local role: This file is a lookup/caching helper. It keeps hot-path code from repeating service calls while preserving the authoritative owner of the data.
- Architecture connection: application files orchestrate use cases and message handling; they may call repositories/clients but should keep transport details at the boundary.
- .NET/C# concepts to notice: IHttpClientFactory centralizes outbound HTTP clients so service-to-service calls get shared base addresses and resilience policy. IMemoryCache is process-local caching; it reduces repeated lookups but is not a source of truth. async/await represents non-blocking I/O; it matters here because database, HTTP, NATS, and SignalR calls should not block server threads.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

using System.Net.Http.Json;
using Alpha.Scada.Contracts;
using Alpha.Scada.ServiceDefaults;
using Alpha.Scada.Telemetry.Contracts;
using Microsoft.Extensions.Caching.Memory;

namespace Alpha.Scada.Alarm.Application;

public sealed class ThresholdCache(IHttpClientFactory httpClientFactory, IMemoryCache cache)
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(1);

    public async Task<IReadOnlyDictionary<string, TagDto>> ResolveAsync(
        Guid tenantId,
        Guid unitId,
        IReadOnlyCollection<StoredSample> samples,
        CancellationToken cancellationToken)
    {
        var tagKeys = samples.Select(sample => sample.TagKey).Distinct().ToArray();
        var cacheKey = $"thresholds:{tenantId}:{unitId}:{string.Join('|', tagKeys.Order())}";
        if (cache.TryGetValue(cacheKey, out IReadOnlyDictionary<string, TagDto>? cached) && cached is not null)
        {
            return cached;
        }

        var response = await httpClientFactory.CreateClient(AlphaServiceClients.TagCatalog)
            .PostAsJsonAsync("/internal/v1/tags/resolve", new ResolveTagsRequest(tenantId, unitId, tagKeys), cancellationToken);
        response.EnsureSuccessStatusCode();
        var tags = await response.Content.ReadFromJsonAsync<IReadOnlyCollection<TagDto>>(cancellationToken) ?? [];
        var tagsByKey = tags.ToDictionary(tag => tag.Key);

        cache.Set(cacheKey, tagsByKey, CacheDuration);
        return tagsByKey;
    }
}
