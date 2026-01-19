using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using ColorSorterGUI.Models;

namespace ColorSorterGUI.Services;

// Repository = en klasse der kapsler database-CRUD for Inventory-tabellen.
// UI/andre services bruger repository i stedet for at skrive SQL direkte.
public class InventoryRepository
{
    // Connection string til SQLite.
    // DatabaseService.GetDbPath() bestemmer hvor .db filen ligger på disken.
    private static string ConnString => $"Data Source={DatabaseService.GetDbPath()}";

    public async Task<Dictionary<ComponentColor, int>> GetCountsAsync()
    {
    // Henter lagerstatus: hvor mange komponenter af hver farve der findes.
    // Returnerer altid mindst Red/Green/Blue i dictionary (default 0),
    // så UI kan vise stabile værdier selv hvis DB mangler en række.
        var result = new Dictionary<ComponentColor, int>
        {
            // Default values hvis DB ikke indeholder alle farver
            [ComponentColor.Red] = 0,
            [ComponentColor.Green] = 0,
            [ComponentColor.Blue] = 0
        };

        await using var connection = new SqliteConnection(ConnString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Color, Count FROM Inventory;";

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            // Kolonne 0: farven som tekst (fx "Red")
            var colorStr = reader.GetString(0);
            // Kolonne 1: count som heltal
            var count = reader.GetInt32(1);
            // Konverter tekst -> enum (ComponentColor)
            // Hvis konvertering lykkes, læg værdien i dictionary
            if (Enum.TryParse<ComponentColor>(colorStr, out var color))
                result[color] = count;
        }

        return result;
    }

    // Ændrer count for en specifik farve med delta (positiv eller negativ).
    // Returnerer den nye count efter opdatering.
    public async Task<int> ChangeCountAsync(ComponentColor color, int delta)
    {
        // Transaktion gør læs+skriv atomisk, så vi undgår "race conditions"
        // hvis flere operationer sker samtidigt.
        await using var connection = new SqliteConnection(ConnString);
        await connection.OpenAsync();

        await using var tx = (SqliteTransaction)await connection.BeginTransactionAsync();


       
        // 1) Læs nuværende count for farven
        await using var read = connection.CreateCommand();
        read.Transaction = tx;
        read.CommandText = "SELECT Count FROM Inventory WHERE Color = $color;";
        read.Parameters.AddWithValue("$color", color.ToString());

        var currentObj = await read.ExecuteScalarAsync();
        // Hvis der ikke findes en række, antag 0
        var current = currentObj is null ? 0 : Convert.ToInt32(currentObj);
        // 2) Beregn næste værdi
        var next = current + delta;
        // Undgå negative counts i lageret
        if (next < 0) next = 0;

        // 3) Skriv tilbage til databasen
        await using var write = connection.CreateCommand();
        write.Transaction = tx;
        write.CommandText = @"
UPDATE Inventory
SET Count = $count,
    UpdatedAtUtc = $now
WHERE Color = $color;";
        write.Parameters.AddWithValue("$count", next);
        write.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
        write.Parameters.AddWithValue("$color", color.ToString());

        await write.ExecuteNonQueryAsync();
        await tx.CommitAsync();

        return next;
    }
}
