using System.Net.Http.Json;
using Alpha.Scada.Contracts;
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

        var response = await httpClientFactory.CreateClient("tagCatalog")
            .PostAsJsonAsync("/internal/v1/tags/resolve", new ResolveTagsRequest(tenantId, unitId, tagKeys), cancellationToken);
        response.EnsureSuccessStatusCode();
        var tags = await response.Content.ReadFromJsonAsync<IReadOnlyCollection<TagDto>>(cancellationToken) ?? [];
        var tagsByKey = tags.ToDictionary(tag => tag.Key);

        cache.Set(cacheKey, tagsByKey, CacheDuration);
        return tagsByKey;
    }
}
