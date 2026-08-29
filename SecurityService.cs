using System.Security.Cryptography;

namespace GoZCCondorLauncher;

public static class SecurityService
{
    public const int MinimumPasswordLength = 6;
    public const int DefaultIterations = 210_000;

    public static PasswordSettings CreatePassword(string password, int iterations = DefaultIterations)
    {
        if (password.Length < MinimumPasswordLength)
            throw new ArgumentException($"Het wachtwoord moet minimaal {MinimumPasswordLength} tekens bevatten.", nameof(password));
        if (iterations < 100_000) throw new ArgumentOutOfRangeException(nameof(iterations));

        var salt = RandomNumberGenerator.GetBytes(32);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, 32);
        return new PasswordSettings
        {
            Algorithm = "PBKDF2-SHA256",
            Iterations = iterations,
            Salt = Convert.ToBase64String(salt),
            Hash = Convert.ToBase64String(hash)
        };
    }

    public static bool VerifyPassword(string password, PasswordSettings? settings)
    {
        if (settings is null || settings.Algorithm != "PBKDF2-SHA256" || settings.Iterations < 100_000) return false;
        try
        {
            var salt = Convert.FromBase64String(settings.Salt);
            var expected = Convert.FromBase64String(settings.Hash);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, settings.Iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException) { return false; }
        catch (ArgumentException) { return false; }
    }
}
