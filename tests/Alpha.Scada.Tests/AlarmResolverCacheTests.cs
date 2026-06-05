/*
ANNOTATION FOR LEARNING:
- File: tests/Alpha.Scada.Tests/AlarmResolverCacheTests.cs
- Module role: Alpha.Scada.Tests is the test suite. These files are executable documentation for architecture decisions, service behavior, and integration edges.
- Local role: This file is a lookup/caching helper. It keeps hot-path code from repeating service calls while preserving the authoritative owner of the data.
- Architecture connection: tests double as executable architecture notes, especially where they verify tenant isolation, broker behavior, schema config, and failure handling.
- .NET/C# concepts to notice: IHttpClientFactory centralizes outbound HTTP clients so service-to-service calls get shared base addresses and resilience policy. xUnit attributes mark tests; Testcontainers spins real Postgres/NATS containers so integration tests exercise production-like dependencies. async/await represents non-blocking I/O; it matters here because database, HTTP, NATS, and SignalR calls should not block server threads.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Alpha.Scada.Alarm.Application;
using Alpha.Scada.Contracts;
using Alpha.Scada.Telemetry.Contracts;
using Microsoft.Extensions.Caching.Memory;

namespace Alpha.Scada.Tests;

// LEARN: declares a class; sealed means no other class can inherit from it.
public sealed class AlarmResolverCacheTests
{
    private static readonly Guid TenantId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid SiteId = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly Guid UnitId = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly Guid TagId = Guid.Parse("40000000-0000-0000-0000-000000000001");

// LEARN: marks this method as a single xUnit test case.
    [Fact]
// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task Unit_key_resolver_loads_route_once_and_uses_cache()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var handler = new RecordingJsonHandler(request =>
        {
// LEARN: branches only when the boolean condition is true.
            if (request.RequestUri?.AbsolutePath == $"/internal/v1/units/{UnitId}/route")
            {
                return new UnitRouteDto(TenantId, SiteId, UnitId, "demo-site", "chp-001", "Combined Heat and Power Unit 001");
            }

// LEARN: branches only when the boolean condition is true.
            if (request.RequestUri?.AbsolutePath == $"/internal/v1/tenants/{TenantId}")
            {
                return new TenantDto(TenantId, "demo-operator", "Demo Operator", "EU");
            }

            return null;
        });
        var resolver = new UnitKeyResolver(new StaticHttpClientFactory(handler), cache);

        var first = await resolver.ResolveAsync(UnitId, CancellationToken.None);
        var second = await resolver.ResolveAsync(UnitId, CancellationToken.None);

// LEARN: asserts expected test behavior; if this condition fails, the test fails.
        Assert.Equal(new UnitRouteKeys(TenantId, UnitId, "demo-operator", "demo-site", "chp-001"), first);
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
        Assert.Equal(first, second);
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
        Assert.Equal(1, handler.Count($"/internal/v1/units/{UnitId}/route"));
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
        Assert.Equal(1, handler.Count($"/internal/v1/tenants/{TenantId}"));
    }

// LEARN: marks this method as a single xUnit test case.
    [Fact]
// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task Threshold_cache_resolves_distinct_tag_keys_once_and_reuses_cached_result()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var handler = new RecordingJsonHandler(request =>
        {
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Equal("/internal/v1/tags/resolve", request.RequestUri?.AbsolutePath);
            return new[]
            {
                new TagDto(TagId, TenantId, UnitId, "engine.electrical_output_kw", "Electrical Output", "Engine", "kW", 45, 70)
            };
        });
        var thresholdCache = new ThresholdCache(new StaticHttpClientFactory(handler), cache);
        var samples = new[]
        {
            new StoredSample(Guid.NewGuid(), "engine.electrical_output_kw", 58, "good", DateTimeOffset.UtcNow),
            new StoredSample(Guid.NewGuid(), "engine.electrical_output_kw", 59, "good", DateTimeOffset.UtcNow)
        };

        var first = await thresholdCache.ResolveAsync(TenantId, UnitId, samples, CancellationToken.None);
        var second = await thresholdCache.ResolveAsync(TenantId, UnitId, samples, CancellationToken.None);

// LEARN: asserts expected test behavior; if this condition fails, the test fails.
        Assert.Single(first);
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
        Assert.Same(first, second);
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
        Assert.Equal(1, handler.Count("/internal/v1/tags/resolve"));
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
        Assert.Single(handler.CapturedResolveRequests.Single().TagKeys);
    }

// LEARN: declares a class; sealed means no other class can inherit from it.
    private sealed class StaticHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
        public HttpClient CreateClient(string name) =>
            new(handler, disposeHandler: false) { BaseAddress = new Uri("http://service.test") };
    }

// LEARN: declares a class; sealed means no other class can inherit from it.
    private sealed class RecordingJsonHandler(Func<HttpRequestMessage, object?> responder) : HttpMessageHandler
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
        private readonly Dictionary<string, int> counts = [];

        public List<ResolveTagsRequest> CapturedResolveRequests { get; } = [];

// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
        public int Count(string path) => counts.GetValueOrDefault(path);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? "";
            counts[path] = counts.GetValueOrDefault(path) + 1;

// LEARN: branches only when the boolean condition is true.
            if (request.Content is not null && path == "/internal/v1/tags/resolve")
            {
                var body = await request.Content.ReadFromJsonAsync<ResolveTagsRequest>(JsonOptions, cancellationToken);
// LEARN: branches only when the boolean condition is true.
                if (body is not null)
                {
                    CapturedResolveRequests.Add(body);
                }
            }

            var response = responder(request);
// LEARN: branches only when the boolean condition is true.
            if (response is null)
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(response, options: JsonOptions)
            };
        }
    }
}
