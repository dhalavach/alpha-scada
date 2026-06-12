namespace Alpha.Scada.Identity.Domain;

public sealed record UserAccount(
    Guid Id,
    Guid TenantId,
    string Email,
    string DisplayName,
    string PasswordHash,
    string Role,
    int FailedLoginCount,
    DateTimeOffset? LockedUntilUtc);
