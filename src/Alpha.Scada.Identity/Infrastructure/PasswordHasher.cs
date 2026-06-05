/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Identity/Infrastructure/PasswordHasher.cs
- Module role: Alpha.Scada.Identity is the identity service. It owns local users, password hashing, role assignment, and JWT issuance, so other services can trust bearer-token claims instead of duplicating credential logic.
- Local role: This file sits in the infrastructure layer, where database, network, and platform details are allowed to appear.
- Architecture connection: infrastructure files are allowed to know about PostgreSQL, SQL, and external protocols because they adapt the outside world to the application model.
- .NET/C# concepts to notice: Authentication/authorization are standard ASP.NET Core concepts: middleware validates tokens, then policies/claims decide access.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using System.Security.Cryptography;

// LEARN: declares the logical namespace; namespaces organize types and help dependency direction stay visible.
namespace Alpha.Scada.Identity.Infrastructure;

// LEARN: declares a static helper class whose members are called on the type itself.
public static class PasswordHasher
{
// LEARN: declares a member with explicit visibility so the type boundary is clear.
    private const int SaltSize = 16;
// LEARN: declares a member with explicit visibility so the type boundary is clear.
    private const int KeySize = 32;
// LEARN: declares a member with explicit visibility so the type boundary is clear.
    private const int Iterations = 100_000;

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    public static string Hash(string password)
    {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
// LEARN: returns a value or exits the current method.
        return $"pbkdf2-sha256.{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(key)}";
    }

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    public static bool Verify(string password, string hash)
    {
// LEARN: starts a protected block whose exceptions can be handled below.
        try
        {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var parts = hash.Split('.');
// LEARN: branches only when the boolean condition is true.
            if (parts.Length != 4 || parts[0] != "pbkdf2-sha256")
            {
// LEARN: returns a value or exits the current method.
                return false;
            }

// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var iterations = int.Parse(parts[1]);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var salt = Convert.FromBase64String(parts[2]);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var expected = Convert.FromBase64String(parts[3]);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
// LEARN: returns a value or exits the current method.
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
// LEARN: handles a specific exception path; filters with when further narrow when it applies.
        catch (Exception ex) when (ex is FormatException or ArgumentException or CryptographicException or OverflowException)
        {
// LEARN: returns a value or exits the current method.
            return false;
        }
    }
}
