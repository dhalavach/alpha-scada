using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Alpha.Scada.Contracts;
using Microsoft.Extensions.Configuration;

namespace Alpha.Scada.ServiceDefaults;

public sealed class JwtTokenService(IConfiguration configuration)
{
    private readonly byte[] _secret = GetSigningSecret(configuration);

    public static byte[] GetSigningSecret(IConfiguration configuration)
    {
        return Encoding.UTF8.GetBytes(
            configuration["Jwt:Secret"]
            ?? "alpha-scada-local-development-secret-change-me-32");
    }

    public LoginResponse Issue(UserDto user, TimeSpan lifetime)
    {
        var expires = DateTimeOffset.UtcNow.Add(lifetime);
        var header = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new { alg = "HS256", typ = "JWT" }));
        var payload = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object?>
        {
            ["sub"] = user.Id,
            ["tenant_id"] = user.TenantId,
            ["email"] = user.Email,
            ["name"] = user.DisplayName,
            ["role"] = user.Role,
            ["exp"] = expires.ToUnixTimeSeconds()
        }));
        var signature = Sign($"{header}.{payload}");
        return new LoginResponse($"{header}.{payload}.{signature}", expires, user);
    }

    public CurrentUserDto? Validate(string token)
    {
        var parts = token.Split('.');
        if (parts.Length != 3)
        {
            return null;
        }

        var expected = Sign($"{parts[0]}.{parts[1]}");
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(parts[2])))
        {
            return null;
        }

        using var document = JsonDocument.Parse(Base64UrlDecode(parts[1]));
        var root = document.RootElement;
        if (root.GetProperty("exp").GetInt64() <= DateTimeOffset.UtcNow.ToUnixTimeSeconds())
        {
            return null;
        }

        return new CurrentUserDto(
            root.GetProperty("sub").GetGuid(),
            root.GetProperty("tenant_id").GetGuid(),
            root.GetProperty("email").GetString() ?? "",
            root.GetProperty("name").GetString() ?? "",
            root.GetProperty("role").GetString() ?? Roles.Viewer);
    }

    private string Sign(string value)
    {
        using var hmac = new HMACSHA256(_secret);
        return Base64Url(hmac.ComputeHash(Encoding.UTF8.GetBytes(value)));
    }

    private static string Base64Url(byte[] bytes)
    {
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + ((4 - padded.Length % 4) % 4), '=');
        return Convert.FromBase64String(padded);
    }
}
