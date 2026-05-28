using System.Net.Http.Json;
using Alpha.Scada.Contracts;
using Microsoft.Extensions.Caching.Memory;

namespace Alpha.Scada.Asset.Application;

public sealed class TenantKeyResolver(IHttpClientFactory httpClientFactory, IMemoryCache cache)
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public async Task<string> ResolveAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var cacheKey = $"tenant-key:{tenantId}";
        if (cache.TryGetValue(cacheKey, out string? cached) && !string.IsNullOrWhiteSpace(cached))
        {
            return cached;
        }

        var tenant = await httpClientFactory.CreateClient("tenant")
            .GetFromJsonAsync<TenantDto>($"/internal/v1/tenants/{tenantId}", cancellationToken)
            ?? throw new InvalidOperationException($"Tenant {tenantId} could not be resolved.");

        cache.Set(cacheKey, tenant.Key, CacheDuration);
        return tenant.Key;
    }
}
