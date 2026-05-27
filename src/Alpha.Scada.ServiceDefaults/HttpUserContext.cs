using Alpha.Scada.Contracts;
using Microsoft.AspNetCore.Http;

namespace Alpha.Scada.ServiceDefaults;

public static class HttpUserContext
{
    public static CurrentUserDto? FromBearerToken(IHeaderDictionary headers, JwtTokenService tokens)
    {
        var header = headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return tokens.Validate(header["Bearer ".Length..].Trim());
    }

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
