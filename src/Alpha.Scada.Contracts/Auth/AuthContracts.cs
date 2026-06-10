namespace Alpha.Scada.Contracts;

public sealed record LoginRequest(string Email, string Password);

public sealed record LoginResponse(string AccessToken, DateTimeOffset ExpiresAtUtc, UserDto User);

public sealed record UserDto(Guid Id, Guid TenantId, string Email, string DisplayName, string Role);

public sealed record CurrentUserDto(Guid UserId, Guid TenantId, string Email, string DisplayName, string Role);

public static class Roles
{
    public const string Admin = "Admin";
    public const string Operator = "Operator";
    public const string Viewer = "Viewer";
    public const string SupportEngineer = "SupportEngineer";
    public const string Service = "Service";
}

public static class RoleRules
{
    public static bool CanAcknowledge(string role) => role is Roles.Admin or Roles.Operator or Roles.SupportEngineer;

    public static bool CanManageConfiguration(string role) => role is Roles.Admin or Roles.SupportEngineer;

    public static bool IsSupport(string role) => role == Roles.SupportEngineer;

    public static bool IsService(string role) => role == Roles.Service;
}
