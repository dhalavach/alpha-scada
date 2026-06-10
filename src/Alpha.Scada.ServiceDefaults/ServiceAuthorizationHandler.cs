using System.Net.Http.Headers;

namespace Alpha.Scada.ServiceDefaults;

public sealed class ServiceAuthorizationHandler(ServiceTokenProvider tokenProvider) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Headers.Authorization is null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenProvider.GetToken());
        }

        return base.SendAsync(request, cancellationToken);
    }
}
