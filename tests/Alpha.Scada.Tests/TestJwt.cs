using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Security.Claims;
using System.Text;
using Alpha.Scada.Contracts;
using Alpha.Scada.ServiceDefaults;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace Alpha.Scada.Tests;

internal static class TestJwt
{
    public const string Secret = "test-secret-test-secret-test-secret-32";

    private static readonly Lazy<KeyMaterial> Keys = new(CreateKeys);

    public static string PublicKeyB64 => Keys.Value.PublicKeyB64;

    public static string PrivateKeyB64 => Keys.Value.PrivateKeyB64;

    public static IConfiguration Configuration(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(Settings(values))
            .Build();

    public static Dictionary<string, string?> Settings(params (string Key, string? Value)[] values)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Jwt:Secret"] = Secret,
            ["Jwt:PublicKeyB64"] = PublicKeyB64,
            ["Jwt:PrivateKeyB64"] = PrivateKeyB64
        };
        foreach (var (key, value) in values)
        {
            settings[key] = value;
        }

        return settings;
    }

    public static string CreateToken(
        string role,
        bool useServiceKey,
        string? issuer = null,
        string? audience = null)
    {
        var userId = Guid.NewGuid();
        var tenantId = role == Roles.Service ? Guid.Empty : Guid.NewGuid();
        SigningCredentials credentials;
        if (useServiceKey)
        {
            credentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret)),
                SecurityAlgorithms.HmacSha256);
        }
        else
        {
            var rsa = RSA.Create();
            rsa.ImportFromPem(Encoding.UTF8.GetString(Convert.FromBase64String(PrivateKeyB64)));
            credentials = new SigningCredentials(new RsaSecurityKey(rsa), SecurityAlgorithms.RsaSha256);
        }

        var token = new JwtSecurityToken(
            issuer ?? (useServiceKey ? JwtTokenService.DefaultServiceIssuer : JwtTokenService.DefaultUserIssuer),
            audience ?? JwtTokenService.DefaultAudience,
            [
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
                new Claim("tenant_id", tenantId.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, "test@example.test"),
                new Claim("name", "Test"),
                new Claim("role", role)
            ],
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static KeyMaterial CreateKeys()
    {
        using var rsa = RSA.Create(2048);
        return new KeyMaterial(
            Convert.ToBase64String(Encoding.UTF8.GetBytes(rsa.ExportSubjectPublicKeyInfoPem())),
            Convert.ToBase64String(Encoding.UTF8.GetBytes(rsa.ExportPkcs8PrivateKeyPem())));
    }

    private sealed record KeyMaterial(string PublicKeyB64, string PrivateKeyB64);
}
