using Xunit.Sdk;

namespace Alpha.Scada.Tests;

internal static class ContainerSupport
{
    private static readonly string[] DockerUnavailableMarkers =
    [
        "cannot connect to the docker daemon",
        "docker daemon",
        "docker desktop",
        "docker endpoint",
        "docker is not available",
        "docker is not running",
        "connection refused"
    ];

    public static async Task<T> StartOrSkipAsync<T>(Func<Task<T>> start, string what)
    {
        try
        {
            return await start();
        }
        catch (Exception ex) when (IsDockerUnavailable(ex))
        {
            throw SkipException.ForSkip($"Docker is not available for {what}: {ex.Message}");
        }
    }

    public static bool IsDockerUnavailable(Exception ex) =>
        ex.GetType().Name == "DockerUnavailableException"
        || ex.GetType().FullName?.StartsWith("Docker.DotNet", StringComparison.Ordinal) == true
        || ex is TimeoutException
        || DockerUnavailableMarkers.Any(marker => ex.Message.Contains(marker, StringComparison.OrdinalIgnoreCase))
        || ex.InnerException is not null && IsDockerUnavailable(ex.InnerException);
}
