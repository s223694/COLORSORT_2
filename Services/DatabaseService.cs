using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace ColorSorterGUI.Services;
// Database service for at håndterering af databaseinitialisering og handlinger. 
public static class DatabaseService
{
    private static string? _dbPath;

    public static string GetDbPath()
    {
        if (_dbPath is not null) return _dbPath;

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appData, "ColorSorterGUI");
        Directory.CreateDirectory(dir);

        _dbPath = Path.Combine(dir, "colorsorter.sqlite");
        return _dbPath;
    }

    public static string GetConnectionString()
    {
        var dbPath = GetDbPath();
        return $"Data Source={dbPath}";
    }

    public static async Task InitializeAsync()
    {
        var connStr = GetConnectionString();

        await using var connection = new SqliteConnection(connStr);
        await connection.OpenAsync();

        // Laver Inventory tabelen hvis den ikke eksisterer
        var createInventory = connection.CreateCommand();
        createInventory.CommandText = @"
CREATE TABLE IF NOT EXISTS Inventory (
    Color TEXT PRIMARY KEY,
    Count INTEGER NOT NULL CHECK (Count >= 0),
    UpdatedAtUtc TEXT NOT NULL
);";
        await createInventory.ExecuteNonQueryAsync();

        

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

        // Laver Users tabelen hvis den ikke eksisterer
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
