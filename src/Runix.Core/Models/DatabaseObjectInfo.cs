using System;

namespace Runix.Core.Models;

public enum DatabaseObjectType
{
    View,
    Trigger,
    Function,
    Procedure,
    Index
}

public class DatabaseObjectInfo
{
    public string Schema { get; set; } = "public";
    public string Name { get; set; } = string.Empty;
    public DatabaseObjectType ObjectType { get; set; }
    public string? TargetTableName { get; set; }
    public string? ReturnType { get; set; }
    public string? Arguments { get; set; }
    public string? Language { get; set; }
    public bool IsUnique { get; set; }
    public string DefinitionDdl { get; set; } = string.Empty;
    public string SpecificId { get; set; } = string.Empty;

    public string QualifiedName =>
        string.IsNullOrWhiteSpace(Schema) || Schema.Equals("main", StringComparison.OrdinalIgnoreCase) || Schema.Equals("public", StringComparison.OrdinalIgnoreCase)
            ? Name
            : $"{Schema}.{Name}";

    public string TypeBadge => ObjectType switch
    {
        DatabaseObjectType.View => "VIEW",
        DatabaseObjectType.Trigger => "TRG",
        DatabaseObjectType.Function => "FUNC",
        DatabaseObjectType.Procedure => "PROC",
        DatabaseObjectType.Index => "IDX",
        _ => "OBJ"
    };

    public string Icon => ObjectType switch
    {
        DatabaseObjectType.View => "👁️",
        DatabaseObjectType.Trigger => "⚡",
        DatabaseObjectType.Function => "ƒ",
        DatabaseObjectType.Procedure => "⚙️",
        DatabaseObjectType.Index => "📇",
        _ => "📄"
    };

    public string Subtitle => ObjectType switch
    {
        DatabaseObjectType.Trigger when !string.IsNullOrWhiteSpace(TargetTableName) => $"on {TargetTableName}",
        DatabaseObjectType.Index when !string.IsNullOrWhiteSpace(TargetTableName) => $"{(IsUnique ? "UNIQUE " : "")}on {TargetTableName}",
        DatabaseObjectType.Function or DatabaseObjectType.Procedure when !string.IsNullOrWhiteSpace(ReturnType) => $"→ {ReturnType}",
        _ => string.Empty
    };
}

public static class DatabaseObjectTemplateGenerator
{
    public static string GenerateTemplate(DatabaseObjectType objectType, DatabaseType dbType, string objectName = "new_object", string? targetTable = null)
    {
        var table = string.IsNullOrWhiteSpace(targetTable) ? "table_name" : targetTable;

        return (objectType, dbType) switch
        {
            (DatabaseObjectType.View, DatabaseType.PostgreSQL) =>
                $"CREATE OR REPLACE VIEW \"{objectName}\" AS\nSELECT *\nFROM \"{table}\"\nLIMIT 100;",

            (DatabaseObjectType.View, _) =>
                $"CREATE VIEW \"{objectName}\" AS\nSELECT *\nFROM \"{table}\";",

            (DatabaseObjectType.Trigger, DatabaseType.PostgreSQL) =>
                $"CREATE OR REPLACE TRIGGER \"{objectName}\"\nAFTER INSERT OR UPDATE ON \"{table}\"\nFOR EACH ROW\nEXECUTE FUNCTION my_trigger_function();",

            (DatabaseObjectType.Trigger, _) =>
                $"CREATE TRIGGER \"{objectName}\"\nAFTER INSERT ON \"{table}\"\nBEGIN\n    -- Insira as instruções SQL da trigger aqui\n    SELECT 1;\nEND;",

            (DatabaseObjectType.Function, _) =>
                $"CREATE OR REPLACE FUNCTION \"{objectName}\"(param_id integer)\nRETURNS integer\nLANGUAGE plpgsql\nAS $$\nBEGIN\n    RETURN param_id;\nEND;\n$$;",

            (DatabaseObjectType.Procedure, _) =>
                $"CREATE OR REPLACE PROCEDURE \"{objectName}\"(param_id integer)\nLANGUAGE plpgsql\nAS $$\nBEGIN\n    -- Corpo da procedure\nEND;\n$$;",

            (DatabaseObjectType.Index, _) =>
                $"CREATE INDEX \"{objectName}\"\nON \"{table}\" (\"id\");",

            _ => $"-- DDL para {objectName}"
        };
    }
}
