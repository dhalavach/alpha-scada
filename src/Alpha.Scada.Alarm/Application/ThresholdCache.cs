/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Alarm/Application/ThresholdCache.cs
- Module role: Alpha.Scada.Alarm is the alarm service. It evaluates domain telemetry/status events against thresholds and quality rules, persists alarm lifecycle state, and publishes alarm changes.
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
using Alpha.Scada.Telemetry.Contracts;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Microsoft.Extensions.Caching.Memory;

// LEARN: declares the logical namespace; namespaces organize types and help dependency direction stay visible.
namespace Alpha.Scada.Alarm.Application;

// LEARN: declares a class; sealed means no other class can inherit from it.
public sealed class ThresholdCache(IHttpClientFactory httpClientFactory, IMemoryCache cache)
{
// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(1);

// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task<IReadOnlyDictionary<string, TagDto>> ResolveAsync(
// LEARN: continues an argument/object/collection initializer onto the next line.
        Guid tenantId,
// LEARN: continues an argument/object/collection initializer onto the next line.
        Guid unitId,
// LEARN: continues an argument/object/collection initializer onto the next line.
        IReadOnlyCollection<StoredSample> samples,
// LEARN: passes cancellation so shutdowns, timeouts, and aborted requests can stop work cooperatively.
        CancellationToken cancellationToken)
    {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var tagKeys = samples.Select(sample => sample.TagKey).Distinct().ToArray();
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var cacheKey = $"thresholds:{tenantId}:{unitId}:{string.Join('|', tagKeys.Order())}";
// LEARN: branches only when the boolean condition is true.
        if (cache.TryGetValue(cacheKey, out IReadOnlyDictionary<string, TagDto>? cached) && cached is not null)
        {
// LEARN: returns a value or exits the current method.
            return cached;
        }

// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var response = await httpClientFactory.CreateClient(AlphaServiceClients.TagCatalog)
// LEARN: performs an outbound HTTP call, usually to another service boundary.
            .PostAsJsonAsync("/internal/v1/tags/resolve", new ResolveTagsRequest(tenantId, unitId, tagKeys), cancellationToken);
// LEARN: executes one C# statement; semicolons terminate most statements.
        response.EnsureSuccessStatusCode();
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var tags = await response.Content.ReadFromJsonAsync<IReadOnlyCollection<TagDto>>(cancellationToken) ?? [];
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var tagsByKey = tags.ToDictionary(tag => tag.Key);

// LEARN: executes one C# statement; semicolons terminate most statements.
        cache.Set(cacheKey, tagsByKey, CacheDuration);
// LEARN: returns a value or exits the current method.
        return tagsByKey;
    }
}
