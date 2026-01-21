using Microsoft.Data.Sqlite;
using System;
using System.Threading.Tasks;

namespace ColorSorterGUI.Services;

// Repository for Users-tabellen.
// Ansvar:
// - Oprette brugere (med hash + salt)
// - Sikre at der findes en default admin-konto
// - Autentificere login ved at sammenligne password mod gemt hash
public sealed class UserRepository
{
    // Connection string gives ind udefra (fx DatabaseService.GetConnectionString()).
    private readonly string _connStr;

    public UserRepository(string connectionString)
    {
        _connStr = connectionString;
    }

    // Sikrer at en bestemt admin-bruger eksisterer.
    // Hvis brugeren allerede findes, gør metoden ingenting.
    public async Task EnsureDefaultAdminAsync(string username, string password)
    {
        await using var conn = new SqliteConnection(_connStr);
        await conn.OpenAsync();

        // Tjek om username allerede findes
        var check = conn.CreateCommand();
        check.CommandText = "SELECT COUNT(*) FROM Users WHERE Username = $u;";
        check.Parameters.AddWithValue("$u", username);

        var exists = (long)await check.ExecuteScalarAsync() > 0;
        if (exists) return;

        // Hash + salt password før lagring (vi gemmer aldrig rå password)
        var (hash, salt) = PasswordHasher.HashPassword(password);

        // Indsæt admin-bruger
        var insert = conn.CreateCommand();
        insert.CommandText = @"
INSERT INTO Users (Username, PasswordHash, PasswordSalt, Role, CreatedAtUtc)
VALUES ($u, $h, $s, $r, $c);";
        insert.Parameters.AddWithValue("$u", username);
        insert.Parameters.AddWithValue("$h", hash);
        insert.Parameters.AddWithValue("$s", salt);
        insert.Parameters.AddWithValue("$r", "Admin");
        insert.Parameters.AddWithValue("$c", DateTime.UtcNow.ToString("o"));

        await insert.ExecuteNonQueryAsync();
    }

    // Login: returnerer (ok, role).
    // - Hvis username ikke findes: ok=false
    // - Hvis password ikke matcher: ok=false
    // - Hvis match: ok=true og role udfyldes
    public async Task<(bool ok, string role)> AuthenticateAsync(string username, string password)
    {
        await using var conn = new SqliteConnection(_connStr);
        await conn.OpenAsync();

        // Hent de data vi skal bruge til at verificere password
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT PasswordHash, PasswordSalt, Role
FROM Users
WHERE Username = $u;";
        cmd.Parameters.AddWithValue("$u", username);

        await using var r = await cmd.ExecuteReaderAsync();

        // Hvis ingen række -> bruger findes ikke
        if (!await r.ReadAsync())
            return (false, "");

        var hash = r.GetString(0);
        var salt = r.GetString(1);
        var role = r.GetString(2);

        // Verificér indtastet password mod (hash+salt)
        var ok = PasswordHasher.VerifyPassword(password, hash, salt);

        // Rolle returneres kun hvis login er OK
        return (ok, ok ? role : "");
    }

    // Opretter en ny bruger.
    // Role valideres i koden for at undgå at der gemmes fx "SuperAdmin" ved en fejl.
    public async Task CreateUserAsync(string username, string password, string role)
    {
        if (role != "Admin" && role != "User")
            throw new ArgumentException("Role must be 'Admin' or 'User'.");

        var (hash, salt) = PasswordHasher.HashPassword(password);

        await using var conn = new SqliteConnection(_connStr);
        await conn.OpenAsync();

        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO Users (Username, PasswordHash, PasswordSalt, Role, CreatedAtUtc)
VALUES ($u, $h, $s, $r, $c);";
        cmd.Parameters.AddWithValue("$u", username);
        cmd.Parameters.AddWithValue("$h", hash);
        cmd.Parameters.AddWithValue("$s", salt);
        cmd.Parameters.AddWithValue("$r", role);
        cmd.Parameters.AddWithValue("$c", DateTime.UtcNow.ToString("o"));

        await cmd.ExecuteNonQueryAsync();
    }
}

