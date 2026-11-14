using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Runix.Infrastructure.Storage;

public static class FavoriteTablesStorage
{
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RUNIX");

    private static readonly string FilePath = Path.Combine(ConfigDir, "favorite_tables.json");

    public static HashSet<string> GetFavorites(string dbIdentifier)
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var dict = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json);
                if (dict != null && dict.TryGetValue(dbIdentifier, out var list))
                {
                    return new HashSet<string>(list, StringComparer.OrdinalIgnoreCase);
                }
            }
        }
        catch { }
        return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    public static void SetFavorites(string dbIdentifier, HashSet<string> favorites)
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            var dict = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                dict = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json) ?? dict;
            }

            dict[dbIdentifier] = new List<string>(favorites);
            var updatedJson = JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(FilePath, updatedJson);
        }
        catch { }
    }
}
