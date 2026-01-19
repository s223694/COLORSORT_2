using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using ColorSorterGUI.Models;

namespace ColorSorterGUI.Services;

public class InventoryRepository
{
    private static string ConnString => $"Data Source={DatabaseService.GetDbPath()}";

    public async Task<Dictionary<ComponentColor, int>> GetCountsAsync()
    {
        var result = new Dictionary<ComponentColor, int>
        {
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
            var colorStr = reader.GetString(0);
            var count = reader.GetInt32(1);

            if (Enum.TryParse<ComponentColor>(colorStr, out var color))
                result[color] = count;
        }

        return result;
    }

    public async Task<int> ChangeCountAsync(ComponentColor color, int delta)
    {
        await using var connection = new SqliteConnection(ConnString);
        await connection.OpenAsync();

        await using var tx = (SqliteTransaction)await connection.BeginTransactionAsync();


        // Read current
        await using var read = connection.CreateCommand();
        read.Transaction = tx;
        read.CommandText = "SELECT Count FROM Inventory WHERE Color = $color;";
        read.Parameters.AddWithValue("$color", color.ToString());

        var currentObj = await read.ExecuteScalarAsync();
        var current = currentObj is null ? 0 : Convert.ToInt32(currentObj);

        var next = current + delta;
        if (next < 0) next = 0;

        // Write back
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
