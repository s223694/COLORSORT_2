using System;
using System.Security.Cryptography;

namespace ColorSorterGUI.Services;

// Password hashing service.
// Bruger PBKDF2 (Rfc2898DeriveBytes) med SHA-256.
// Designet til at være langsom nok til at gøre brute-force angreb dyre,
// men stadig hurtig nok til en desktop-applikation.
public static class PasswordHasher
{
    // Salt størrelse i bytes (16 bytes = 128 bit).
    // Salt sikrer at samme password aldrig giver samme hash to gange.
    private const int SaltSize = 16;

    // Længden af den afledte nøgle (hash) i bytes (32 bytes = 256 bit).
    private const int KeySize = 32;

    // Antal iterationer PBKDF2 kører.
    // Højt tal = langsommere hashing = bedre mod brute-force.
    // 200.000 er et fornuftigt niveau for desktop apps.
    private const int Iterations = 200_000;

    // Hasher et password og returnerer både hash og salt i Base64-format,
    // så de nemt kan gemmes som tekst i databasen.
    public static (string hashB64, string saltB64) HashPassword(string password)
    {
        // Genererer kryptografisk tilfældigt salt
        var salt = RandomNumberGenerator.GetBytes(SaltSize);

        // PBKDF2: afleder en sikker nøgle fra password + salt
        using var pbkdf2 = new Rfc2898DeriveBytes(
            password,
            salt,
            Iterations,
            HashAlgorithmName.SHA256);

        var hash = pbkdf2.GetBytes(KeySize);

        // Returnerer hash og salt som Base64-strenge
        return (
            Convert.ToBase64String(hash),
            Convert.ToBase64String(salt)
        );
    }

    // Verificerer et indtastet password ved login.
    // Samme salt og parametre bruges til at genskabe hash,
    // som derefter sammenlignes med den gemte hash.
    public static bool VerifyPassword(string password, string hashB64, string saltB64)
    {
        // Genskab salt og forventet hash fra databasen
        var salt = Convert.FromBase64String(saltB64);
        var expectedHash = Convert.FromBase64String(hashB64);

        using var pbkdf2 = new Rfc2898DeriveBytes(
            password,
            salt,
            Iterations,
            HashAlgorithmName.SHA256);

        var actualHash = pbkdf2.GetBytes(KeySize);

        // FixedTimeEquals beskytter mod timing attacks
        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }
}
