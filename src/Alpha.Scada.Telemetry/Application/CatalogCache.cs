/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Telemetry/Application/CatalogCache.cs
- Module role: Alpha.Scada.Telemetry is the historian and normalization boundary. It converts raw edge payloads into canonical readings, resolves tags, persists current/history rows, and emits domain events after storage.
- Local role: This file is a lookup/caching helper. It keeps hot-path code from repeating service calls while preserving the authoritative owner of the data.
- Architecture connection: application files orchestrate use cases and message handling; they may call repositories/clients but should keep transport details at the boundary.
- .NET/C# concepts to notice: IHttpClientFactory centralizes outbound HTTP clients so service-to-service calls get shared base addresses and resilience policy. IMemoryCache is process-local caching; it reduces repeated lookups but is not a source of truth. C# records are concise immutable data carriers with value-based equality, useful for DTOs and domain events. async/await represents non-blocking I/O; it matters here because database, HTTP, NATS, and SignalR calls should not block server threads.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using System.Net.Http.Json;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Contracts;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.ServiceDefaults;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Telemetry.Application.Messaging;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Microsoft.Extensions.Caching.Memory;

// LEARN: declares the logical namespace; namespaces organize types and help dependency direction stay visible.
namespace Alpha.Scada.Telemetry.Application;

// LEARN: declares a class; sealed means no other class can inherit from it.
public sealed class CatalogCache(IHttpClientFactory httpClientFactory, IMemoryCache cache)
{
// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(1);

// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task<ResolvedTelemetryBatch?> ResolveAsync(CanonicalTelemetry telemetry, CancellationToken cancellationToken)
    {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var tenant = await ResolveTenantAsync(telemetry.TenantKey, cancellationToken);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var unit = await ResolveUnitAsync(tenant.Id, telemetry.SiteKey, telemetry.UnitKey, cancellationToken);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var tagKeys = telemetry.Readings.Select(sample => sample.TagKey).Distinct().ToArray();
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var tags = await ResolveTagsAsync(tenant.Id, unit.UnitId, tagKeys, cancellationToken);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var tagsByKey = tags.ToDictionary(tag => tag.Key);

        // TODO: Unknown tags are currently dropped. Follow-up: quarantine or auto-provision them.
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var samples = telemetry.Readings
// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
            .Where(sample => tagsByKey.ContainsKey(sample.TagKey))
// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
            .Select(sample =>
            {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
                var tag = tagsByKey[sample.TagKey];
// LEARN: returns a value or exits the current method.
                return new ResolvedTelemetrySample(
// LEARN: continues an argument/object/collection initializer onto the next line.
                    tag.Id,
// LEARN: continues an argument/object/collection initializer onto the next line.
                    tag.Key,
// LEARN: continues an argument/object/collection initializer onto the next line.
                    tag.Name,
// LEARN: continues an argument/object/collection initializer onto the next line.
                    tag.Subsystem,
// LEARN: continues an argument/object/collection initializer onto the next line.
                    tag.EngineeringUnit,
// LEARN: continues an argument/object/collection initializer onto the next line.
                    tag.AlarmLow,
// LEARN: continues an argument/object/collection initializer onto the next line.
                    tag.AlarmHigh,
// LEARN: continues an argument/object/collection initializer onto the next line.
                    sample.Value,
// LEARN: continues an argument/object/collection initializer onto the next line.
                    sample.Quality,
// LEARN: executes one C# statement; semicolons terminate most statements.
                    sample.SourceTimestampUtc);
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
            })
// LEARN: executes one C# statement; semicolons terminate most statements.
            .ToArray();

// LEARN: returns a value or exits the current method.
        return samples.Length == 0
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
            ? null
// LEARN: creates a new object or record instance.
            : new ResolvedTelemetryBatch(
// LEARN: continues an argument/object/collection initializer onto the next line.
                tenant.Id,
// LEARN: continues an argument/object/collection initializer onto the next line.
                unit.SiteId,
// LEARN: continues an argument/object/collection initializer onto the next line.
                unit.UnitId,
// LEARN: continues an argument/object/collection initializer onto the next line.
                telemetry.TenantKey,
// LEARN: continues an argument/object/collection initializer onto the next line.
                telemetry.SiteKey,
// LEARN: continues an argument/object/collection initializer onto the next line.
                telemetry.UnitKey,
// LEARN: continues an argument/object/collection initializer onto the next line.
                unit.UnitName,
// LEARN: executes one C# statement; semicolons terminate most statements.
                samples);
    }

// LEARN: declares an asynchronous method that can await non-blocking I/O.
    private async Task<TenantDto> ResolveTenantAsync(string tenantKey, CancellationToken cancellationToken)
    {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var cacheKey = $"tenant:{tenantKey}";
// LEARN: branches only when the boolean condition is true.
        if (cache.TryGetValue(cacheKey, out TenantDto? cached) && cached is not null)
        {
// LEARN: returns a value or exits the current method.
            return cached;
        }

// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var tenant = await httpClientFactory.CreateClient(AlphaServiceClients.Tenant)
// LEARN: performs an outbound HTTP call, usually to another service boundary.
            .GetFromJsonAsync<TenantDto>($"/internal/v1/tenants/resolve/{tenantKey}", cancellationToken)
// LEARN: creates a new object or record instance.
            ?? throw new InvalidOperationException($"Tenant {tenantKey} is not allow-listed.");

// LEARN: executes one C# statement; semicolons terminate most statements.
        cache.Set(cacheKey, tenant, CacheDuration);
// LEARN: returns a value or exits the current method.
        return tenant;
    }

// LEARN: declares an asynchronous method that can await non-blocking I/O.
    private async Task<ResolvedUnitDto> ResolveUnitAsync(Guid tenantId, string siteKey, string unitKey, CancellationToken cancellationToken)
    {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var cacheKey = $"unit:{tenantId}:{siteKey}:{unitKey}";
// LEARN: branches only when the boolean condition is true.
        if (cache.TryGetValue(cacheKey, out ResolvedUnitDto? cached) && cached is not null)
        {
// LEARN: returns a value or exits the current method.
            return cached;
        }

// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var unit = await httpClientFactory.CreateClient(AlphaServiceClients.Asset)
// LEARN: performs an outbound HTTP call, usually to another service boundary.
            .GetFromJsonAsync<ResolvedUnitDto>(
// LEARN: continues an argument/object/collection initializer onto the next line.
                $"/internal/v1/units/resolve?tenantId={tenantId}&siteKey={siteKey}&unitKey={unitKey}",
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
                cancellationToken)
// LEARN: creates a new object or record instance.
            ?? throw new InvalidOperationException($"Unit {tenantId}/{siteKey}/{unitKey} is not allow-listed.");

// LEARN: executes one C# statement; semicolons terminate most statements.
        cache.Set(cacheKey, unit, CacheDuration);
// LEARN: returns a value or exits the current method.
        return unit;
    }

// LEARN: declares an asynchronous method that can await non-blocking I/O.
    private async Task<IReadOnlyCollection<TagDto>> ResolveTagsAsync(
// LEARN: continues an argument/object/collection initializer onto the next line.
        Guid tenantId,
// LEARN: continues an argument/object/collection initializer onto the next line.
        Guid unitId,
// LEARN: continues an argument/object/collection initializer onto the next line.
        IReadOnlyCollection<string> tagKeys,
// LEARN: passes cancellation so shutdowns, timeouts, and aborted requests can stop work cooperatively.
        CancellationToken cancellationToken)
    {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var cacheKey = $"tags:{tenantId}:{unitId}:{string.Join('|', tagKeys.Order())}";
// LEARN: branches only when the boolean condition is true.
        if (cache.TryGetValue(cacheKey, out IReadOnlyCollection<TagDto>? cached) && cached is not null)
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

// LEARN: executes one C# statement; semicolons terminate most statements.
        cache.Set(cacheKey, tags, CacheDuration);
// LEARN: returns a value or exits the current method.
        return tags;
    }
}

// LEARN: declares an immutable C# record, commonly used for DTOs and message contracts.
public sealed record ResolvedTelemetryBatch(
// LEARN: continues an argument/object/collection initializer onto the next line.
    Guid TenantId,
// LEARN: continues an argument/object/collection initializer onto the next line.
    Guid SiteId,
// LEARN: continues an argument/object/collection initializer onto the next line.
    Guid UnitId,
// LEARN: continues an argument/object/collection initializer onto the next line.
    string TenantKey,
// LEARN: continues an argument/object/collection initializer onto the next line.
    string SiteKey,
// LEARN: continues an argument/object/collection initializer onto the next line.
    string UnitKey,
// LEARN: continues an argument/object/collection initializer onto the next line.
    string UnitName,
// LEARN: executes one C# statement; semicolons terminate most statements.
    IReadOnlyCollection<ResolvedTelemetrySample> Samples);
