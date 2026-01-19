using System;
using System.Security.Cryptography;

namespace ColorSorterGUI.Services;
// Password hashing service using PBKDF2 with SHA-256
public static class PasswordHasher
{
    private const int SaltSize = 16;        // 128-bit
    private const int KeySize = 32;         // 256-bit
    private const int Iterations = 200_000; // ok for desktop apps

    public static (string hashB64, string saltB64) HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);

        using var pbkdf2 = new Rfc2898DeriveBytes(
            password,
            salt,
            Iterations,
            HashAlgorithmName.SHA256);

        var hash = pbkdf2.GetBytes(KeySize);

        return (Convert.ToBase64String(hash), Convert.ToBase64String(salt));
    }

    public static bool VerifyPassword(string password, string hashB64, string saltB64)
    {
        var salt = Convert.FromBase64String(saltB64);
        var expectedHash = Convert.FromBase64String(hashB64);

        using var pbkdf2 = new Rfc2898DeriveBytes(
            password,
            salt,
            Iterations,
            HashAlgorithmName.SHA256);

        var actualHash = pbkdf2.GetBytes(KeySize);

        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }
}
