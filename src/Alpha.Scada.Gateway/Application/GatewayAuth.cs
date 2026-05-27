using Alpha.Scada.Contracts;
using Alpha.Scada.ServiceDefaults;

namespace Alpha.Scada.Gateway.Application;

public static class GatewayAuth
{
    public static CurrentUserDto? Authenticate(HttpContext context, JwtTokenService tokens)
    {
        var header = context.Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return tokens.Validate(header["Bearer ".Length..].Trim());
    }

    public static HttpRequestMessage WithBearerToken(this HttpRequestMessage request, HttpContext context)
    {
        request.ForwardAuthorizationFrom(context.Request);
        return request;
    }
}
