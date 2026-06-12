using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Alpha.Scada.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using JsonWebToken = Microsoft.IdentityModel.JsonWebTokens.JsonWebToken;

namespace Alpha.Scada.ServiceDefaults;

public sealed class JwtTokenService(IConfiguration configuration)
{
    public const string DefaultUserIssuer = "alpha-scada-identity";
    public const string DefaultServiceIssuer = "alpha-scada-service";
    public const string DefaultAudience = "alpha-scada";

    private readonly JwtSecurityTokenHandler handler = new() { MapInboundClaims = false };
    private readonly SymmetricSecurityKey serviceSigningKey = new(GetSigningSecret(configuration));
    private readonly TokenValidationParameters validationParameters = CreateValidationParameters(configuration);
    private readonly Lazy<SigningCredentials> userSigningCredentials = new(
        () => CreateUserSigningCredentials(configuration),
        LazyThreadSafetyMode.ExecutionAndPublication);

    public static byte[] GetSigningSecret(IConfiguration configuration)
    {
        var secret = configuration["Jwt:Secret"];
        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new InvalidOperationException("Jwt:Secret must be configured. Set JWT_SECRET in .env or Jwt__Secret in the environment.");
        }

        return Encoding.UTF8.GetBytes(secret);
    }

    public static TokenValidationParameters CreateValidationParameters(IConfiguration configuration)
    {
        var rsaKey = new RsaSecurityKey(LoadRsaKey(configuration, "Jwt:PublicKeyB64", includePrivateParameters: false));
        var symmetricKey = new SymmetricSecurityKey(GetSigningSecret(configuration));
        return new TokenValidationParameters
        {
            IssuerSigningKeys = [rsaKey, symmetricKey],
            ValidIssuers = [UserIssuer(configuration), ServiceIssuer(configuration)],
            ValidAudience = Audience(configuration),
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256, SecurityAlgorithms.HmacSha256],
            ValidateIssuerSigningKey = true,
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "name",
            RoleClaimType = "role"
        };
    }

    public LoginResponse IssueUserToken(UserDto user, TimeSpan lifetime)
    {
        if (user.Role == Roles.Service)
        {
            throw new InvalidOperationException("Service principals must use HS256 service tokens.");
        }

        return Issue(user, lifetime, UserIssuer(configuration), userSigningCredentials.Value);
    }

    public LoginResponse IssueServiceToken(UserDto user, TimeSpan lifetime)
    {
        if (user.Role != Roles.Service)
        {
            throw new InvalidOperationException("HS256 tokens may only be issued for the Service role.");
        }

        return Issue(
            user,
            lifetime,
            ServiceIssuer(configuration),
            new SigningCredentials(serviceSigningKey, SecurityAlgorithms.HmacSha256));
    }

    public CurrentUserDto? Validate(string token)
    {
        try
        {
            var principal = handler.ValidateToken(token, validationParameters, out var validatedToken);
            if (!HasCompatibleRoleAlgorithmAndIssuer(
                    GetAlgorithm(validatedToken),
                    principal.FindFirstValue("role"),
                    GetIssuer(validatedToken),
                    configuration))
            {
                return null;
            }

            var userId = Guid.Parse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? "");
            var tenantId = Guid.Parse(principal.FindFirstValue("tenant_id") ?? "");
            return new CurrentUserDto(
                userId,
                tenantId,
                principal.FindFirstValue(JwtRegisteredClaimNames.Email) ?? "",
                principal.FindFirstValue("name") ?? "",
                principal.FindFirstValue("role") ?? Roles.Viewer);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or SecurityTokenException)
        {
            return null;
        }
    }

    public static bool HasCompatibleRoleAlgorithmAndIssuer(
        string? algorithm,
        string? role,
        string? issuer,
        IConfiguration configuration) =>
        algorithm switch
        {
            SecurityAlgorithms.HmacSha256 =>
                role == Roles.Service && issuer == ServiceIssuer(configuration),
            SecurityAlgorithms.RsaSha256 =>
                IsUserRole(role) && issuer == UserIssuer(configuration),
            _ => false
        };

    public static string GetAlgorithm(SecurityToken token) =>
        token switch
        {
            JwtSecurityToken jwt => jwt.Header.Alg,
            JsonWebToken json => json.Alg,
            _ => string.Empty
        };

    public static string GetIssuer(SecurityToken token) =>
        token switch
        {
            JwtSecurityToken jwt => jwt.Issuer,
            JsonWebToken json => json.Issuer,
            _ => string.Empty
        };

    private LoginResponse Issue(
        UserDto user,
        TimeSpan lifetime,
        string issuer,
        SigningCredentials credentials)
    {
        var expires = DateTimeOffset.UtcNow.Add(lifetime);
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = Audience(configuration),
            Subject = new ClaimsIdentity([
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim("tenant_id", user.TenantId.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                new Claim("name", user.DisplayName),
                new Claim("role", user.Role)
            ]),
            Expires = expires.UtcDateTime,
            SigningCredentials = credentials
        };
        return new LoginResponse(handler.WriteToken(handler.CreateToken(descriptor)), expires, user);
    }

    private static SigningCredentials CreateUserSigningCredentials(IConfiguration configuration) =>
        new(
            new RsaSecurityKey(LoadRsaKey(configuration, "Jwt:PrivateKeyB64", includePrivateParameters: true)),
            SecurityAlgorithms.RsaSha256);

    private static RSA LoadRsaKey(
        IConfiguration configuration,
        string configurationKey,
        bool includePrivateParameters)
    {
        var encoded = configuration[configurationKey];
        if (string.IsNullOrWhiteSpace(encoded))
        {
            var message = includePrivateParameters
                ? "Jwt:PrivateKeyB64 is required to issue user tokens - only the Identity service should configure it."
                : "Jwt:PublicKeyB64 is required to validate user tokens.";
            throw new InvalidOperationException(message);
        }

        try
        {
            var pem = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
            var rsa = RSA.Create();
            rsa.ImportFromPem(pem);
            if (includePrivateParameters && rsa.ExportParameters(includePrivateParameters: true).D is null)
            {
                rsa.Dispose();
                throw new InvalidOperationException("Jwt:PrivateKeyB64 does not contain an RSA private key.");
            }

            return rsa;
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            throw new InvalidOperationException($"{configurationKey} is not a valid base64-encoded RSA PEM key.", ex);
        }
    }

    private static string UserIssuer(IConfiguration configuration) =>
        configuration["Jwt:UserIssuer"] ?? DefaultUserIssuer;

    private static string ServiceIssuer(IConfiguration configuration) =>
        configuration["Jwt:ServiceIssuer"] ?? DefaultServiceIssuer;

    private static string Audience(IConfiguration configuration) =>
        configuration["Jwt:Audience"] ?? DefaultAudience;

    private static bool IsUserRole(string? role) =>
        role is Roles.Admin or Roles.Operator or Roles.Viewer or Roles.SupportEngineer;
}

public static class JwtTokenServiceRegistration
{
    public static IServiceCollection AddJwtTokenService(this IServiceCollection services, IConfiguration configuration)
    {
        _ = JwtTokenService.GetSigningSecret(configuration);
        _ = JwtTokenService.CreateValidationParameters(configuration);
        services.TryAddSingleton(_ => new JwtTokenService(configuration));
        return services;
    }
}
