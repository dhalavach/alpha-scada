using Alpha.Scada.Api.Data;

namespace Alpha.Scada.Api.Modules.Auth;

public static class AuthEndpointFilter
{
    public static async Task<IResult> WithUser(
        HttpContext context,
        AuthService auth,
        Func<CurrentUser, Task<IResult>> handler)
    {
        var user = await auth.AuthenticateAsync(context);
        return user is null
            ? Results.Unauthorized()
            : await handler(user);
    }

    public static bool CanAcknowledge(CurrentUser user)
    {
        return user.Role is Roles.Admin or Roles.Operator or Roles.SupportEngineer;
    }

    public static bool CanAdmin(CurrentUser user)
    {
        return user.Role is Roles.Admin or Roles.SupportEngineer;
    }
}
