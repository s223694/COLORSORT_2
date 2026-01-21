using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace ColorSorterGUI.Services;

// DatabaseService håndterer:
// - hvor databasefilen ligger (path)
// - connection string
// - initialisering: oprette tabeller + indsætte startdata (seed)
public static class DatabaseService
{
    // Cache af database-path så vi ikke beregner den flere gange.
    private static string? _dbPath;

    // Finder (og gemmer) stien til databasen.
    // Vi lægger den i ApplicationData (AppData/Roaming på Windows),
    // så den ikke ligger i projektmappen men i brugerens dataområde.
    public static string GetDbPath()
    {
        if (_dbPath is not null) return _dbPath;

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appData, "ColorSorterGUI");

        // Opretter mappen hvis den ikke findes
        Directory.CreateDirectory(dir);

        // Selve SQLite filen
        _dbPath = Path.Combine(dir, "colorsorter.sqlite");
        return _dbPath;
    }

    // Returnerer standard SQLite connection string for den valgte db-fil.
    public static string GetConnectionString()
    {
        var dbPath = GetDbPath();
        return $"Data Source={dbPath}";
    }

    // Initialiserer databasen:
    // - Opretter tabeller hvis de ikke findes
    // - Seeder inventory med standardfarver, så resten af systemet kan antage at rækker findes
    public static async Task InitializeAsync()
    {
        var connStr = GetConnectionString();

        await using var connection = new SqliteConnection(connStr);
        await connection.OpenAsync();

        // 1) Opret Inventory-tabel hvis den ikke eksisterer
        var createInventory = connection.CreateCommand();
        createInventory.CommandText = @"
CREATE TABLE IF NOT EXISTS Inventory (
    Color TEXT PRIMARY KEY,
    Count INTEGER NOT NULL CHECK (Count >= 0),
    UpdatedAtUtc TEXT NOT NULL
);";
        await createInventory.ExecuteNonQueryAsync();

        // 2) Seed Inventory med de farver systemet forventer findes.
        // ON CONFLICT DO NOTHING betyder at vi ikke overskriver hvis rækken allerede eksisterer.
        foreach (var color in new[] { "Red", "Green", "Blue" })
        {
            var seed = connection.CreateCommand();
            seed.CommandText = @"
INSERT INTO Inventory (Color, Count, UpdatedAtUtc)
VALUES ($color, 0, $now)
ON CONFLICT(Color) DO NOTHING;";
            seed.Parameters.AddWithValue("$color", color);
            seed.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
            await seed.ExecuteNonQueryAsync();
        }

        // 3) Opret Users-tabel hvis den ikke eksisterer
        // Bruges til login/roller (Admin/User)
        var createUsers = connection.CreateCommand();
        createUsers.CommandText = @"
CREATE TABLE IF NOT EXISTS Users (
  Id INTEGER PRIMARY KEY AUTOINCREMENT,
  Username TEXT NOT NULL UNIQUE,
  PasswordHash TEXT NOT NULL,
  PasswordSalt TEXT NOT NULL,
  Role TEXT NOT NULL,            -- 'Admin' or 'User'
  CreatedAtUtc TEXT NOT NULL
);";
        await createUsers.ExecuteNonQueryAsync();
    }
}
