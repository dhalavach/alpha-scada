namespace Alpha.Scada.Api.Modules.Auth;

public sealed record CurrentUser(
    Guid UserId,
    Guid TenantId,
    string Email,
    string DisplayName,
    string Role);
