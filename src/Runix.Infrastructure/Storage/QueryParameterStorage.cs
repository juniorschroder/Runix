using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Runix.Core.Services;

namespace Runix.Infrastructure.Storage;

/// <summary>
/// Persists recent query parameter history across application sessions in ~/.runix or AppData.
/// </summary>
public static class QueryParameterStorage
{
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RUNIX");

    private static readonly string FilePath = Path.Combine(ConfigDir, "query_parameters_history.json");
    private static readonly object _fileLock = new();

    public static void LoadIntoService(IQueryParameterService service)
    {
        try
        {
            lock (_fileLock)
            {
                if (!File.Exists(FilePath)) return;

                var json = File.ReadAllText(FilePath);
                var dict = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json);
                if (dict == null) return;

                foreach (var (paramName, values) in dict)
                {
                    if (values == null) continue;
                    // Reverse to keep order when inserting into service
                    for (int i = values.Count - 1; i >= 0; i--)
                    {
                        service.SaveHistory(paramName, values[i]);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[QueryParameterStorage] Failed to load history: {ex.Message}");
        }
    }

    public static void SaveFromService(IQueryParameterService service, IEnumerable<string> parameterNames)
    {
        try
        {
            lock (_fileLock)
            {
                Directory.CreateDirectory(ConfigDir);

                var dict = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

                if (File.Exists(FilePath))
                {
                    try
                    {
                        var existingJson = File.ReadAllText(FilePath);
                        var existing = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(existingJson);
                        if (existing != null)
                        {
                            foreach (var (k, v) in existing)
                                dict[k] = v;
                        }
                    }
                    catch { }
                }

                foreach (var param in parameterNames)
                {
                    var hist = service.GetHistory(param);
                    if (hist.Count > 0)
                    {
                        dict[param] = new List<string>(hist);
                    }
                }

                var options = new JsonSerializerOptions { WriteIndented = true };
                var serialized = JsonSerializer.Serialize(dict, options);
                File.WriteAllText(FilePath, serialized);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[QueryParameterStorage] Failed to save history: {ex.Message}");
        }
    }
}
