/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Alarm/Application/UnitKeyResolver.cs
- Module role: Alpha.Scada.Alarm is the alarm service. It evaluates domain telemetry/status events against thresholds and quality rules, persists alarm lifecycle state, and publishes alarm changes.
- Local role: This file is a lookup/caching helper. It keeps hot-path code from repeating service calls while preserving the authoritative owner of the data.
- Architecture connection: application files orchestrate use cases and message handling; they may call repositories/clients but should keep transport details at the boundary.
- .NET/C# concepts to notice: IHttpClientFactory centralizes outbound HTTP clients so service-to-service calls get shared base addresses and resilience policy. IMemoryCache is process-local caching; it reduces repeated lookups but is not a source of truth. C# records are concise immutable data carriers with value-based equality, useful for DTOs and domain events. async/await represents non-blocking I/O; it matters here because database, HTTP, NATS, and SignalR calls should not block server threads.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

using System.Net.Http.Json;
using Alpha.Scada.Contracts;
using Alpha.Scada.ServiceDefaults;
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

        var asset = httpClientFactory.CreateClient(AlphaServiceClients.Asset);
        var route = await asset.GetFromJsonAsync<UnitRouteDto>($"/internal/v1/units/{unitId}/route", cancellationToken)
            ?? throw new InvalidOperationException($"Unit route {unitId} could not be resolved.");

        var tenant = await httpClientFactory.CreateClient(AlphaServiceClients.Tenant)
            .GetFromJsonAsync<TenantDto>($"/internal/v1/tenants/{route.TenantId}", cancellationToken)
            ?? throw new InvalidOperationException($"Tenant route {route.TenantId} could not be resolved.");

        var resolved = new UnitRouteKeys(route.TenantId, route.UnitId, tenant.Key, route.SiteKey, route.UnitKey);
        cache.Set(cacheKey, resolved, CacheDuration);
        return resolved;
    }
}

public sealed record UnitRouteKeys(Guid TenantId, Guid UnitId, string TenantKey, string SiteKey, string UnitKey);
