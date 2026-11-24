using System;
using System.Collections.Generic;

namespace Runix.Core.Models.MockData;

/// <summary>
/// Configuração de geração sintética para uma coluna específica.
/// </summary>
public class ColumnGeneratorRule
{
    public string ColumnName { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public bool IsNullable { get; set; }
    public bool IsPrimaryKey { get; set; }
    public bool IsAutoIncrement { get; set; }

    public GeneratorType SelectedGenerator { get; set; } = GeneratorType.Name;
    public int NullPercentage { get; set; } = 0; // 0 a 100%

    // Numeric parameters
    public long? MinValue { get; set; }
    public long? MaxValue { get; set; }
    public int? Decimals { get; set; }

    // Date/Time parameters
    public DateTime? DateStart { get; set; }
    public DateTime? DateEnd { get; set; }

    // Choice / Enum parameters
    public List<string> Choices { get; set; } = new();

    // Foreign Key sampling parameters
    public string? ForeignKeyTargetTable { get; set; }
    public string? ForeignKeyTargetColumn { get; set; }
    public List<object> SampledValues { get; set; } = new();
    public bool ParentTableEmpty { get; set; }

    // Sequence & Custom
    public long SequenceStart { get; set; } = 1;
    public long SequenceStep { get; set; } = 1;
    public string? CustomFormat { get; set; }

    // Skip flag (e.g. for identity or auto-increment)
    public bool IsSkipped { get; set; }

    public override string ToString() => $"{ColumnName} ({DataType}) -> {SelectedGenerator}{(IsSkipped ? " [SKIPPED]" : "")}";
}
