/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Asset/Application/TenantKeyResolver.cs
- Module role: Alpha.Scada.Asset is the asset service. It owns sites, units, unit lookup by route key, online/offline status, and the bridge from stored telemetry events into operational unit health.
- Local role: This file is a lookup/caching helper. It keeps hot-path code from repeating service calls while preserving the authoritative owner of the data.
- Architecture connection: application files orchestrate use cases and message handling; they may call repositories/clients but should keep transport details at the boundary.
- .NET/C# concepts to notice: IHttpClientFactory centralizes outbound HTTP clients so service-to-service calls get shared base addresses and resilience policy. IMemoryCache is process-local caching; it reduces repeated lookups but is not a source of truth. async/await represents non-blocking I/O; it matters here because database, HTTP, NATS, and SignalR calls should not block server threads.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

using System.Net.Http.Json;
using Alpha.Scada.Contracts;
using Alpha.Scada.ServiceDefaults;
using Microsoft.Extensions.Caching.Memory;

namespace Alpha.Scada.Asset.Application;

// LEARN: declares a class; sealed means no other class can inherit from it.
public sealed class TenantKeyResolver(IHttpClientFactory httpClientFactory, IMemoryCache cache)
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task<string> ResolveAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var cacheKey = $"tenant-key:{tenantId}";
// LEARN: branches only when the boolean condition is true.
        if (cache.TryGetValue(cacheKey, out string? cached) && !string.IsNullOrWhiteSpace(cached))
        {
            return cached;
        }

        var tenant = await httpClientFactory.CreateClient(AlphaServiceClients.Tenant)
// LEARN: performs an outbound HTTP call, usually to another service boundary.
            .GetFromJsonAsync<TenantDto>($"/internal/v1/tenants/{tenantId}", cancellationToken)
            ?? throw new InvalidOperationException($"Tenant {tenantId} could not be resolved.");

        cache.Set(cacheKey, tenant.Key, CacheDuration);
        return tenant.Key;
    }
}
