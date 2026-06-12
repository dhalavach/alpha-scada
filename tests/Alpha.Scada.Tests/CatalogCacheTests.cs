using System.Net;
using System.Net.Http.Json;
using System.Text;
using Alpha.Scada.Contracts;
using Alpha.Scada.ServiceDefaults;
using Alpha.Scada.Telemetry.Application;
using Alpha.Scada.Telemetry.Application.Messaging;
using Microsoft.Extensions.Caching.Memory;

namespace Alpha.Scada.Tests;

public sealed class CatalogCacheTests
{
    private static readonly Guid TenantId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid SiteId = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly Guid UnitId = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly Guid TagId = Guid.Parse("40000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task Confirmed_tenant_miss_is_negative_cached()
    {
        var calls = 0;
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var catalog = new CatalogCache(
            Factory((_, _) =>
            {
                Interlocked.Increment(ref calls);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }),
            cache,
            new TelemetryIngestionMetrics());

        await Assert.ThrowsAsync<TelemetryResolutionException>(() => catalog.ResolveAsync(Telemetry("missing"), CancellationToken.None));
        await Assert.ThrowsAsync<TelemetryResolutionException>(() => catalog.ResolveAsync(Telemetry("missing"), CancellationToken.None));

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Transient_catalog_failure_is_not_negative_cached()
    {
        var calls = 0;
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var catalog = new CatalogCache(
            Factory((_, _) =>
            {
                Interlocked.Increment(ref calls);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            }),
            cache,
            new TelemetryIngestionMetrics());

        await Assert.ThrowsAsync<HttpRequestException>(() => catalog.ResolveAsync(Telemetry("demo"), CancellationToken.None));
        await Assert.ThrowsAsync<HttpRequestException>(() => catalog.ResolveAsync(Telemetry("demo"), CancellationToken.None));

        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Unknown_tag_samples_are_dropped_and_metered()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var metrics = new TelemetryIngestionMetrics();
        var catalog = new CatalogCache(
            Factory((request, _) =>
            {
                if (request.RequestUri?.AbsolutePath.StartsWith("/internal/v1/tenants/resolve/", StringComparison.Ordinal) == true)
                {
                    return Task.FromResult(Json(new TenantDto(TenantId, "demo", "Demo", "EU")));
                }

                if (request.RequestUri?.AbsolutePath == "/internal/v1/units/resolve")
                {
                    return Task.FromResult(Json(new ResolvedUnitDto(TenantId, SiteId, UnitId, "Unit", "online")));
                }

                return Task.FromResult(Json<IReadOnlyCollection<TagDto>>([
                    new(TagId, TenantId, UnitId, "known", "Known", "Engine", "kW", null, null)
                ]));
            }),
            cache,
            metrics);

        var resolved = await catalog.ResolveAsync(
            Telemetry("demo", new CanonicalReading("known", 1, "good", DateTimeOffset.UtcNow),
                new CanonicalReading("unknown", 2, "good", DateTimeOffset.UtcNow)),
            CancellationToken.None);

        Assert.NotNull(resolved);
        Assert.Single(resolved.Samples);
        var rendered = new StringBuilder();
        metrics.AppendMetrics(rendered, "telemetry-test");
        Assert.Contains("alpha_scada_telemetry_ingestion_unknown_tags_dropped_total{service=\"telemetry-test\"} 1", rendered.ToString());
    }

    private static CanonicalTelemetry Telemetry(string tenantKey, params CanonicalReading[] readings) =>
        new(
            tenantKey,
            "site/key",
            "unit key",
            DateTimeOffset.UtcNow,
            readings.Length == 0
                ? [new CanonicalReading("known", 1, "good", DateTimeOffset.UtcNow)]
                : readings);

    private static IHttpClientFactory Factory(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) =>
        new StaticHttpClientFactory(new DelegateHandler(responder));

    private static HttpResponseMessage Json<T>(T value) =>
        new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };

    private sealed class StaticHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(handler, disposeHandler: false) { BaseAddress = new Uri("http://catalog.test") };
    }

    private sealed class DelegateHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            responder(request, cancellationToken);
    }
}
