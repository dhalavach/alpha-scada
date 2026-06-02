using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Alpha.Scada.Alarm.Application;
using Alpha.Scada.Contracts;
using Alpha.Scada.Telemetry.Contracts;
using Microsoft.Extensions.Caching.Memory;

namespace Alpha.Scada.Tests;

public sealed class AlarmResolverCacheTests
{
    private static readonly Guid TenantId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid SiteId = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly Guid UnitId = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly Guid TagId = Guid.Parse("40000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task Unit_key_resolver_loads_route_once_and_uses_cache()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var handler = new RecordingJsonHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath == $"/internal/v1/units/{UnitId}/route")
            {
                return new UnitRouteDto(TenantId, SiteId, UnitId, "demo-site", "chp-001", "Combined Heat and Power Unit 001");
            }

            if (request.RequestUri?.AbsolutePath == $"/internal/v1/tenants/{TenantId}")
            {
                return new TenantDto(TenantId, "demo-operator", "Demo Operator", "EU");
            }

            return null;
        });
        var resolver = new UnitKeyResolver(new StaticHttpClientFactory(handler), cache);

        var first = await resolver.ResolveAsync(UnitId, CancellationToken.None);
        var second = await resolver.ResolveAsync(UnitId, CancellationToken.None);

        Assert.Equal(new UnitRouteKeys(TenantId, UnitId, "demo-operator", "demo-site", "chp-001"), first);
        Assert.Equal(first, second);
        Assert.Equal(1, handler.Count($"/internal/v1/units/{UnitId}/route"));
        Assert.Equal(1, handler.Count($"/internal/v1/tenants/{TenantId}"));
    }

    [Fact]
    public async Task Threshold_cache_resolves_distinct_tag_keys_once_and_reuses_cached_result()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var handler = new RecordingJsonHandler(request =>
        {
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

        Assert.Single(first);
        Assert.Same(first, second);
        Assert.Equal(1, handler.Count("/internal/v1/tags/resolve"));
        Assert.Single(handler.CapturedResolveRequests.Single().TagKeys);
    }

    private sealed class StaticHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(handler, disposeHandler: false) { BaseAddress = new Uri("http://service.test") };
    }

    private sealed class RecordingJsonHandler(Func<HttpRequestMessage, object?> responder) : HttpMessageHandler
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
        private readonly Dictionary<string, int> counts = [];

        public List<ResolveTagsRequest> CapturedResolveRequests { get; } = [];

        public int Count(string path) => counts.GetValueOrDefault(path);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? "";
            counts[path] = counts.GetValueOrDefault(path) + 1;

            if (request.Content is not null && path == "/internal/v1/tags/resolve")
            {
                var body = await request.Content.ReadFromJsonAsync<ResolveTagsRequest>(JsonOptions, cancellationToken);
                if (body is not null)
                {
                    CapturedResolveRequests.Add(body);
                }
            }

            var response = responder(request);
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
