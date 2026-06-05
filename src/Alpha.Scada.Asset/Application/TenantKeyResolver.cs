/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Asset/Application/TenantKeyResolver.cs
- Module role: Alpha.Scada.Asset is the asset service. It owns sites, units, unit lookup by route key, online/offline status, and the bridge from stored telemetry events into operational unit health.
- Local role: This file is a lookup/caching helper. It keeps hot-path code from repeating service calls while preserving the authoritative owner of the data.
- Architecture connection: application files orchestrate use cases and message handling; they may call repositories/clients but should keep transport details at the boundary.
- .NET/C# concepts to notice: IHttpClientFactory centralizes outbound HTTP clients so service-to-service calls get shared base addresses and resilience policy. IMemoryCache is process-local caching; it reduces repeated lookups but is not a source of truth. async/await represents non-blocking I/O; it matters here because database, HTTP, NATS, and SignalR calls should not block server threads.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using System.Net.Http.Json;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Contracts;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.ServiceDefaults;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Microsoft.Extensions.Caching.Memory;

// LEARN: declares the logical namespace; namespaces organize types and help dependency direction stay visible.
namespace Alpha.Scada.Asset.Application;

// LEARN: declares a class; sealed means no other class can inherit from it.
public sealed class TenantKeyResolver(IHttpClientFactory httpClientFactory, IMemoryCache cache)
{
// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task<string> ResolveAsync(Guid tenantId, CancellationToken cancellationToken)
    {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var cacheKey = $"tenant-key:{tenantId}";
// LEARN: branches only when the boolean condition is true.
        if (cache.TryGetValue(cacheKey, out string? cached) && !string.IsNullOrWhiteSpace(cached))
        {
// LEARN: returns a value or exits the current method.
            return cached;
        }

// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var tenant = await httpClientFactory.CreateClient(AlphaServiceClients.Tenant)
// LEARN: performs an outbound HTTP call, usually to another service boundary.
            .GetFromJsonAsync<TenantDto>($"/internal/v1/tenants/{tenantId}", cancellationToken)
// LEARN: creates a new object or record instance.
            ?? throw new InvalidOperationException($"Tenant {tenantId} could not be resolved.");

// LEARN: executes one C# statement; semicolons terminate most statements.
        cache.Set(cacheKey, tenant.Key, CacheDuration);
// LEARN: returns a value or exits the current method.
        return tenant.Key;
    }
}
