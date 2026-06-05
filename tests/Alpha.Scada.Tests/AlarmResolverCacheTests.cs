/*
ANNOTATION FOR LEARNING:
- File: tests/Alpha.Scada.Tests/AlarmResolverCacheTests.cs
- Module role: Alpha.Scada.Tests is the test suite. These files are executable documentation for architecture decisions, service behavior, and integration edges.
- Local role: This file is a lookup/caching helper. It keeps hot-path code from repeating service calls while preserving the authoritative owner of the data.
- Architecture connection: tests double as executable architecture notes, especially where they verify tenant isolation, broker behavior, schema config, and failure handling.
- .NET/C# concepts to notice: IHttpClientFactory centralizes outbound HTTP clients so service-to-service calls get shared base addresses and resilience policy. xUnit attributes mark tests; Testcontainers spins real Postgres/NATS containers so integration tests exercise production-like dependencies. async/await represents non-blocking I/O; it matters here because database, HTTP, NATS, and SignalR calls should not block server threads.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using System.Net;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using System.Net.Http.Json;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using System.Text.Json;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Alarm.Application;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Contracts;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Telemetry.Contracts;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Microsoft.Extensions.Caching.Memory;

// LEARN: declares the logical namespace; namespaces organize types and help dependency direction stay visible.
namespace Alpha.Scada.Tests;

// LEARN: declares a class; sealed means no other class can inherit from it.
public sealed class AlarmResolverCacheTests
{
// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private static readonly Guid TenantId = Guid.Parse("10000000-0000-0000-0000-000000000001");
// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private static readonly Guid SiteId = Guid.Parse("20000000-0000-0000-0000-000000000001");
// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private static readonly Guid UnitId = Guid.Parse("30000000-0000-0000-0000-000000000001");
// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private static readonly Guid TagId = Guid.Parse("40000000-0000-0000-0000-000000000001");

// LEARN: marks this method as a single xUnit test case.
    [Fact]
// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task Unit_key_resolver_loads_route_once_and_uses_cache()
    {
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
        using var cache = new MemoryCache(new MemoryCacheOptions());
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var handler = new RecordingJsonHandler(request =>
        {
// LEARN: branches only when the boolean condition is true.
            if (request.RequestUri?.AbsolutePath == $"/internal/v1/units/{UnitId}/route")
            {
// LEARN: returns a value or exits the current method.
                return new UnitRouteDto(TenantId, SiteId, UnitId, "demo-site", "chp-001", "Combined Heat and Power Unit 001");
            }

// LEARN: branches only when the boolean condition is true.
            if (request.RequestUri?.AbsolutePath == $"/internal/v1/tenants/{TenantId}")
            {
// LEARN: returns a value or exits the current method.
                return new TenantDto(TenantId, "demo-operator", "Demo Operator", "EU");
            }

// LEARN: returns a value or exits the current method.
            return null;
// LEARN: executes one C# statement; semicolons terminate most statements.
        });
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var resolver = new UnitKeyResolver(new StaticHttpClientFactory(handler), cache);

// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var first = await resolver.ResolveAsync(UnitId, CancellationToken.None);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
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
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
        using var cache = new MemoryCache(new MemoryCacheOptions());
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var handler = new RecordingJsonHandler(request =>
        {
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Equal("/internal/v1/tags/resolve", request.RequestUri?.AbsolutePath);
// LEARN: returns a value or exits the current method.
            return new[]
            {
// LEARN: creates a new object or record instance.
                new TagDto(TagId, TenantId, UnitId, "engine.electrical_output_kw", "Electrical Output", "Engine", "kW", 45, 70)
            };
// LEARN: executes one C# statement; semicolons terminate most statements.
        });
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var thresholdCache = new ThresholdCache(new StaticHttpClientFactory(handler), cache);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var samples = new[]
        {
// LEARN: creates a new object or record instance.
            new StoredSample(Guid.NewGuid(), "engine.electrical_output_kw", 58, "good", DateTimeOffset.UtcNow),
// LEARN: creates a new object or record instance.
            new StoredSample(Guid.NewGuid(), "engine.electrical_output_kw", 59, "good", DateTimeOffset.UtcNow)
        };

// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var first = await thresholdCache.ResolveAsync(TenantId, UnitId, samples, CancellationToken.None);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
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
// LEARN: creates a new object or record instance.
            new(handler, disposeHandler: false) { BaseAddress = new Uri("http://service.test") };
    }

// LEARN: declares a class; sealed means no other class can inherit from it.
    private sealed class RecordingJsonHandler(Func<HttpRequestMessage, object?> responder) : HttpMessageHandler
    {
// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
// LEARN: declares a member with explicit visibility so the type boundary is clear.
        private readonly Dictionary<string, int> counts = [];

// LEARN: declares a property; get/init or get-only accessors expose data while controlling mutation.
        public List<ResolveTagsRequest> CapturedResolveRequests { get; } = [];

// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
        public int Count(string path) => counts.GetValueOrDefault(path);

// LEARN: passes cancellation so shutdowns, timeouts, and aborted requests can stop work cooperatively.
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var path = request.RequestUri?.AbsolutePath ?? "";
// LEARN: executes one C# statement; semicolons terminate most statements.
            counts[path] = counts.GetValueOrDefault(path) + 1;

// LEARN: branches only when the boolean condition is true.
            if (request.Content is not null && path == "/internal/v1/tags/resolve")
            {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
                var body = await request.Content.ReadFromJsonAsync<ResolveTagsRequest>(JsonOptions, cancellationToken);
// LEARN: branches only when the boolean condition is true.
                if (body is not null)
                {
// LEARN: executes one C# statement; semicolons terminate most statements.
                    CapturedResolveRequests.Add(body);
                }
            }

// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var response = responder(request);
// LEARN: branches only when the boolean condition is true.
            if (response is null)
            {
// LEARN: returns a value or exits the current method.
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

// LEARN: returns a value or exits the current method.
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
                Content = JsonContent.Create(response, options: JsonOptions)
            };
        }
    }
}
