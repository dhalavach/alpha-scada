using System.Reflection;
using Alpha.Scada.Contracts;

namespace Alpha.Scada.ServiceDefaults;

public sealed class ServiceTokenProvider(JwtTokenService tokens)
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan RenewalSkew = TimeSpan.FromMinutes(1);
    private readonly object gate = new();
    private readonly Guid principalId = Guid.NewGuid();
    private LoginResponse? current;

    public string GetToken()
    {
        lock (gate)
        {
            if (current is null || current.ExpiresAtUtc - RenewalSkew <= DateTimeOffset.UtcNow)
            {
                var serviceName = Assembly.GetEntryAssembly()?.GetName().Name ?? "alpha-service";
                current = tokens.IssueServiceToken(
                    new UserDto(principalId, Guid.Empty, $"{serviceName}@internal", serviceName, Roles.Service),
                    Lifetime);
            }

            return current.AccessToken;
        }
    }
}
