using System.Security.Cryptography;
using System.Text;
using Alpha.Scada.Api.Contracts;
using Alpha.Scada.Api.Data;

namespace Alpha.Scada.Api.Modules.Auth;

public sealed class AuthService(PlatformRepository repository)
{
    public async Task<LoginResponse?> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var user = await repository.GetUserByEmailAsync(request.Email, cancellationToken);
        if (user is null || !PasswordHasher.Verify(request.Password, user.PasswordHash))
        {
            await repository.WriteAuditAsync(null, null, "auth.login_failed", $"Login failed for {request.Email}", cancellationToken);
            return null;
        }

        var token = CreateToken();
        var expiresAt = DateTimeOffset.UtcNow.AddHours(12);
        await repository.CreateSessionAsync(user.Id, HashToken(token), expiresAt, cancellationToken);
        await repository.WriteAuditAsync(user.TenantId, user.Id, "auth.login", "User logged in", cancellationToken);

        return new LoginResponse(
            token,
            expiresAt,
            new UserDto(user.Id, user.TenantId, user.Email, user.DisplayName, user.Role));
    }

    public async Task LogoutAsync(string token, CurrentUser user, CancellationToken cancellationToken)
    {
        await repository.DeleteSessionAsync(HashToken(token), cancellationToken);
        await repository.WriteAuditAsync(user.TenantId, user.UserId, "auth.logout", "User logged out", cancellationToken);
    }

    public async Task<CurrentUser?> AuthenticateAsync(HttpContext context)
    {
        var header = context.Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var token = header["Bearer ".Length..].Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        return await repository.GetUserBySessionAsync(HashToken(token), context.RequestAborted);
    }

    public static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes);
    }

    private static string CreateToken()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
    }
}
