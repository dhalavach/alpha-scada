using Microsoft.AspNetCore.Http;

namespace Alpha.Scada.ServiceDefaults;

public static class HttpUserContext
{
    public static void ForwardAuthorizationFrom(this HttpRequestMessage request, HttpRequest source)
    {
        request.ForwardAuthorization(source.Headers.Authorization.ToString());
    }

    public static void ForwardAuthorization(this HttpRequestMessage request, string authorizationHeader)
    {
        request.Headers.Remove("Authorization");
        if (!string.IsNullOrWhiteSpace(authorizationHeader))
        {
            request.Headers.TryAddWithoutValidation("Authorization", authorizationHeader);
        }
    }
}
