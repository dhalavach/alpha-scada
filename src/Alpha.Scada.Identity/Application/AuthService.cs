using Alpha.Scada.Contracts;
using Alpha.Scada.Identity.Infrastructure;
using Alpha.Scada.ServiceDefaults;

namespace Alpha.Scada.Identity.Application;

public sealed class AuthService(
    IdentityRepository repository,
    JwtTokenService tokens,
    TimeProvider timeProvider)
{
    public const int MaximumPasswordLength = 1_024;

    public async Task<LoginResponse?> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var user = await repository.GetByEmailAsync(request.Email, cancellationToken);
        if (user?.LockedUntilUtc > timeProvider.GetUtcNow())
        {
            await repository.WriteAuditAsync(
                user.TenantId,
                user.Id,
                "auth.login_locked",
                "Login rejected while account was locked",
                cancellationToken);
            return null;
        }

        var passwordValid = request.Password.Length <= MaximumPasswordLength
            && user is not null
            && PasswordHasher.Verify(request.Password, user.PasswordHash);
        if (!passwordValid)
        {
            if (user is not null)
            {
                await repository.RecordFailedLoginAsync(user.Id, cancellationToken);
            }

            await repository.WriteAuditAsync(null, null, "auth.login_failed", $"Login failed for {request.Email}", cancellationToken);
            return null;
        }

        var authenticatedUser = user!;
        await repository.ResetLoginFailuresAsync(authenticatedUser.Id, cancellationToken);
        if (PasswordHasher.NeedsRehash(authenticatedUser.PasswordHash))
        {
            await repository.UpdatePasswordHashAsync(authenticatedUser.Id, PasswordHasher.Hash(request.Password), cancellationToken);
        }

        await repository.WriteAuditAsync(
            authenticatedUser.TenantId,
            authenticatedUser.Id,
            "auth.login",
            "User logged in",
            cancellationToken);
        return tokens.IssueUserToken(IdentityRepository.ToDto(authenticatedUser), TimeSpan.FromHours(12));
    }
}
