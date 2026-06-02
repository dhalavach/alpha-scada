using Alpha.Scada.ServiceDefaults;
using Microsoft.Extensions.DependencyInjection;

namespace Alpha.Scada.Tests;

public sealed class HttpClientResilienceTests
{
    [Fact]
    public void Alpha_resilience_policy_registers_for_named_http_client()
    {
        var services = new ServiceCollection();

        services
            .AddHttpClient("downstream", client => client.BaseAddress = new Uri("http://localhost"))
            .AddAlphaResilience();

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        using var client = factory.CreateClient("downstream");

        Assert.Equal(new Uri("http://localhost/"), client.BaseAddress);
    }
}
