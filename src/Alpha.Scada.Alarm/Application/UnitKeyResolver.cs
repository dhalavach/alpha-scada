using System.Net.Http.Json;
using Alpha.Scada.Contracts;
using Microsoft.Extensions.Caching.Memory;

namespace Alpha.Scada.Alarm.Application;

public sealed class UnitKeyResolver(IHttpClientFactory httpClientFactory, IMemoryCache cache)
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public async Task<UnitRouteKeys> ResolveAsync(Guid unitId, CancellationToken cancellationToken)
    {
        var cacheKey = $"unit-route:{unitId}";
        if (cache.TryGetValue(cacheKey, out UnitRouteKeys? cached) && cached is not null)
        {
            return cached;
        }

        var asset = httpClientFactory.CreateClient("asset");
        var route = await asset.GetFromJsonAsync<UnitRouteDto>($"/internal/v1/units/{unitId}/route", cancellationToken)
            ?? throw new InvalidOperationException($"Unit route {unitId} could not be resolved.");

        var tenant = await httpClientFactory.CreateClient("tenant")
            .GetFromJsonAsync<TenantDto>($"/internal/v1/tenants/{route.TenantId}", cancellationToken)
            ?? throw new InvalidOperationException($"Tenant route {route.TenantId} could not be resolved.");

        var resolved = new UnitRouteKeys(route.TenantId, route.UnitId, tenant.Key, route.SiteKey, route.UnitKey);
        cache.Set(cacheKey, resolved, CacheDuration);
        return resolved;
    }
}

public sealed record UnitRouteKeys(Guid TenantId, Guid UnitId, string TenantKey, string SiteKey, string UnitKey);
