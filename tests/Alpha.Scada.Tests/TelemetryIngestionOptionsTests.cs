using Alpha.Scada.Telemetry.Application;
using Microsoft.Extensions.Configuration;

namespace Alpha.Scada.Tests;

public sealed class TelemetryIngestionOptionsTests
{
    [Fact]
    public void Missing_config_defaults_to_eight()
    {
        var options = TelemetryIngestionOptions.FromConfiguration(new ConfigurationBuilder().Build());

        Assert.Equal(8, options.EffectiveMaxDegreeOfParallelism);
    }

    [Fact]
    public void Configured_value_below_one_clamps_to_one()
    {
        var options = TelemetryIngestionOptions.FromConfiguration(Configuration("-4"));

        Assert.Equal(1, options.EffectiveMaxDegreeOfParallelism);
    }

    [Fact]
    public void Configured_value_above_sixty_four_clamps_to_sixty_four()
    {
        var options = TelemetryIngestionOptions.FromConfiguration(Configuration("100"));

        Assert.Equal(64, options.EffectiveMaxDegreeOfParallelism);
    }

    private static IConfiguration Configuration(string maxDegreeOfParallelism) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Telemetry:Ingestion:MaxDegreeOfParallelism"] = maxDegreeOfParallelism
            })
            .Build();
}
