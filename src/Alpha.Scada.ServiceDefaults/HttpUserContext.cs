using Alpha.Scada.Contracts;
using Microsoft.AspNetCore.Http;

namespace Alpha.Scada.ServiceDefaults;

public static class HttpUserContext
{
    public static CurrentUserDto? FromHeaders(IHeaderDictionary headers)
    {
        return Guid.TryParse(headers["X-User-Id"], out var userId)
            && Guid.TryParse(headers["X-Tenant-Id"], out var tenantId)
            ? new CurrentUserDto(
                userId,
                tenantId,
                headers["X-User-Email"].ToString(),
                headers["X-User-Name"].ToString(),
                headers["X-User-Role"].ToString())
            : null;
    }

    public static void AddUserHeaders(this HttpRequestMessage request, CurrentUserDto user)
    {
        request.Headers.Remove("X-User-Id");
        request.Headers.Remove("X-Tenant-Id");
        request.Headers.Remove("X-User-Email");
        request.Headers.Remove("X-User-Name");
        request.Headers.Remove("X-User-Role");
        request.Headers.Add("X-User-Id", user.UserId.ToString());
        request.Headers.Add("X-Tenant-Id", user.TenantId.ToString());
        request.Headers.Add("X-User-Email", user.Email);
        request.Headers.Add("X-User-Name", user.DisplayName);
        request.Headers.Add("X-User-Role", user.Role);
    }
}
