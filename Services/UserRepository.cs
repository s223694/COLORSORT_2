using Microsoft.Data.Sqlite;
using System;
using System.Threading.Tasks;

namespace ColorSorterGUI.Services;
// User repository for managing user accounts and authentication.
public sealed class UserRepository
{
    private readonly string _connStr;

    public UserRepository(string connectionString)
    {
        _connStr = connectionString;
    }

    public async Task EnsureDefaultAdminAsync(string username, string password)
    {
        await using var conn = new SqliteConnection(_connStr);
        await conn.OpenAsync();

        var check = conn.CreateCommand();
        check.CommandText = "SELECT COUNT(*) FROM Users WHERE Username = $u;";
        check.Parameters.AddWithValue("$u", username);
        var exists = (long)await check.ExecuteScalarAsync() > 0;
        if (exists) return;

        var (hash, salt) = PasswordHasher.HashPassword(password);

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

    public async Task<(bool ok, string role)> AuthenticateAsync(string username, string password)
    {
        await using var conn = new SqliteConnection(_connStr);
        await conn.OpenAsync();

        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT PasswordHash, PasswordSalt, Role
FROM Users
WHERE Username = $u;";
        cmd.Parameters.AddWithValue("$u", username);

        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync())
            return (false, "");

        var hash = r.GetString(0);
        var salt = r.GetString(1);
        var role = r.GetString(2);

        var ok = PasswordHasher.VerifyPassword(password, hash, salt);
        return (ok, ok ? role : "");
    }

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
