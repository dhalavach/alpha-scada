using System.Net.Http.Json;
using Alpha.Scada.Contracts;

namespace Alpha.Scada.Edge.Application;

public static class GatewayRealtimeClient
{
    public static async Task PostRealtimeAsync(
        this HttpClient gateway,
        string path,
        RealtimeNotificationRequest notification,
        string? serviceToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(notification)
        };

        if (!string.IsNullOrWhiteSpace(serviceToken))
        {
            request.Headers.TryAddWithoutValidation("X-Service-Token", serviceToken);
        }

        using var response = await gateway.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
