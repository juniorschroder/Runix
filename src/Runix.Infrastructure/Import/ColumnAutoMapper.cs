using System;
using System.Collections.Generic;
using System.Linq;
using Runix.Core.Models;

namespace Runix.Infrastructure.Import;

/// <summary>
/// Mapeador inteligente de correspondência entre cabeçalhos de arquivo e colunas do banco de dados.
/// </summary>
public static class ColumnAutoMapper
{
    public static List<ColumnMapping> AutoMapColumns(
        IReadOnlyList<string> sourceHeaders,
        IReadOnlyList<ColumnInfo> tableColumns)
    {
        var mappings = new List<ColumnMapping>();
        var usedTargetColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var header in sourceHeaders)
        {
            var cleanHeader = header.Trim();
            if (string.IsNullOrEmpty(cleanHeader))
            {
                mappings.Add(new ColumnMapping { SourceColumn = header, TargetColumn = null });
                continue;
            }

            // 1. Exact match (case-insensitive)
            var exactMatch = tableColumns.FirstOrDefault(c => 
                !usedTargetColumns.Contains(c.Name) && 
                c.Name.Equals(cleanHeader, StringComparison.OrdinalIgnoreCase));

            if (exactMatch != null)
            {
                usedTargetColumns.Add(exactMatch.Name);
                mappings.Add(new ColumnMapping
                {
                    SourceColumn = header,
                    TargetColumn = exactMatch.Name,
                    TargetDataType = exactMatch.DataType
                });
                continue;
            }

            // 2. Normalized match (remove underscores, dashes, spaces)
            var normalizedHeader = Normalize(cleanHeader);
            var normalizedMatch = tableColumns.FirstOrDefault(c => 
                !usedTargetColumns.Contains(c.Name) && 
                Normalize(c.Name).Equals(normalizedHeader, StringComparison.OrdinalIgnoreCase));

            if (normalizedMatch != null)
            {
                usedTargetColumns.Add(normalizedMatch.Name);
                mappings.Add(new ColumnMapping
                {
                    SourceColumn = header,
                    TargetColumn = normalizedMatch.Name,
                    TargetDataType = normalizedMatch.DataType
                });
                continue;
            }

            // 3. Fallback: No match found -> Ignored
            mappings.Add(new ColumnMapping
            {
                SourceColumn = header,
                TargetColumn = null
            });
        }

        return mappings;
    }

    private static string Normalize(string name)
    {
        return name.Replace("_", "")
                   .Replace("-", "")
                   .Replace(" ", "")
                   .ToLowerInvariant();
    }
}
