/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Identity/Infrastructure/PasswordHasher.cs
- Module role: Alpha.Scada.Identity is the identity service. It owns local users, password hashing, role assignment, and JWT issuance, so other services can trust bearer-token claims instead of duplicating credential logic.
- Local role: This file sits in the infrastructure layer, where database, network, and platform details are allowed to appear.
- Architecture connection: infrastructure files are allowed to know about PostgreSQL, SQL, and external protocols because they adapt the outside world to the application model.
- .NET/C# concepts to notice: Authentication/authorization are standard ASP.NET Core concepts: middleware validates tokens, then policies/claims decide access.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

using System.Security.Cryptography;

namespace Alpha.Scada.Identity.Infrastructure;

// LEARN: declares a static helper class whose members are called on the type itself.
public static class PasswordHasher
{
    private const int SaltSize = 16;
    private const int KeySize = 32;
    private const int Iterations = 100_000;

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        return $"pbkdf2-sha256.{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(key)}";
    }

    public static bool Verify(string password, string hash)
    {
// LEARN: starts a protected block whose exceptions can be handled below.
        try
        {
            var parts = hash.Split('.');
// LEARN: branches only when the boolean condition is true.
            if (parts.Length != 4 || parts[0] != "pbkdf2-sha256")
            {
                return false;
            }

            var iterations = int.Parse(parts[1]);
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
// LEARN: handles a specific exception path; filters with when further narrow when it applies.
        catch (Exception ex) when (ex is FormatException or ArgumentException or CryptographicException or OverflowException)
        {
            return false;
        }
    }
}
