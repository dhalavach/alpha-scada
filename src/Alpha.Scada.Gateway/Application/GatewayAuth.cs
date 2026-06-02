using Alpha.Scada.ServiceDefaults;

namespace Alpha.Scada.Gateway.Application;

public static class GatewayAuth
{
    public static HttpRequestMessage WithBearerToken(this HttpRequestMessage request, HttpContext context)
    {
        request.ForwardAuthorizationFrom(context.Request);
        return request;
    }
}
