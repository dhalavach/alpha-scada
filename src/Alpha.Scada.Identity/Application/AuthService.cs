using Alpha.Scada.Contracts;
using Alpha.Scada.Identity.Infrastructure;
using Alpha.Scada.ServiceDefaults;

namespace Alpha.Scada.Identity.Application;

public sealed class AuthService(IdentityRepository repository, JwtTokenService tokens)
{
    public async Task<LoginResponse?> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var user = await repository.GetByEmailAsync(request.Email, cancellationToken);
        if (user is null || !PasswordHasher.Verify(request.Password, user.PasswordHash))
        {
            await repository.WriteAuditAsync(null, null, "auth.login_failed", $"Login failed for {request.Email}", cancellationToken);
            return null;
        }

        await repository.WriteAuditAsync(user.TenantId, user.Id, "auth.login", "User logged in", cancellationToken);
        return tokens.Issue(IdentityRepository.ToDto(user), TimeSpan.FromHours(12));
    }
}
