using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Runix.Core.Interfaces;
using Runix.Core.Models;
using Runix.Core.Models.MockData;
using Runix.Core.Services;

namespace Runix.Infrastructure.Services;

/// <summary>
/// Motor de geração sintética de dados relacionais com suporte a inferência semântica,
/// integridade referencial via amostragem de FK e inserção segura em lotes parametrizados.
/// </summary>
public class MockDataGeneratorService : IMockDataGeneratorService
{
    private static readonly Random Rnd = new();

    #region Mock Data Datasets

    private static readonly string[] FirstNames =
    {
        "Lucas", "Mateus", "Gabriel", "Rodrigo", "Felipe", "Mariana", "Beatriz", "Camila",
        "Larissa", "Juliana", "Gustavo", "Eduardo", "Carolina", "Fernanda", "Thiago", "Amanda",
        "Bruno", "Rafael", "Vinicius", "Daniela", "James", "John", "Emma", "Sophia", "Oliver",
        "Alexander", "Liam", "Charlotte", "Amelia", "Noah", "Elena", "Marcus", "Diana", "Victor"
    };

    private static readonly string[] LastNames =
    {
        "Silva", "Santos", "Oliveira", "Souza", "Pereira", "Lima", "Ferreira", "Costa",
        "Rodrigues", "Almeida", "Nascimento", "Alves", "Carvalho", "Mendes", "Ribeiro", "Araujo",
        "Smith", "Johnson", "Brown", "Taylor", "Miller", "Wilson", "Davis", "Martinez", "Anderson"
    };

    private static readonly string[] StreetPrefixes =
    {
        "Rua", "Avenida", "Travessa", "Alameda", "Praça"
    };

    private static readonly string[] StreetNames =
    {
        "Paulista", "Augusta", "das Flores", "Brasil", "XV de Novembro", "Sete de Setembro",
        "Independência", "Rio Branco", "Tiradentes", "Bela Cintra", "Consolação", "Getúlio Vargas",
        "Broadway", "Main Street", "Park Avenue", "Oak Street", "Pine Street", "Maple Avenue"
    };

    private static readonly string[] Cities =
    {
        "São Paulo", "Rio de Janeiro", "Belo Horizonte", "Curitiba", "Porto Alegre",
        "Salvador", "Fortaleza", "Brasília", "Recife", "Florianópolis", "Campinas",
        "New York", "San Francisco", "London", "Toronto", "Berlin", "Tokyo"
    };

    private static readonly string[] Countries =
    {
        "Brasil", "Estados Unidos", "Canadá", "Alemanha", "França",
        "Reino Unido", "Japão", "Portugal", "Espanha", "Itália", "Austrália"
    };

    private static readonly string[] StatusChoices =
    {
        "ativo", "inativo", "pendente", "cancelado"
    };

    #endregion

    /// <summary>
    /// Infere regras semânticas baseando-se em nomes de coluna, tipos de dados e relacionamentos de chave estrangeira.
    /// </summary>
    public List<ColumnGeneratorRule> InferRules(TableInfo table, List<ColumnInfo> columns, List<ForeignKeyInfo> foreignKeys)
    {
        var rules = new List<ColumnGeneratorRule>();

        foreach (var col in columns)
        {
            var rule = new ColumnGeneratorRule
            {
                ColumnName = col.Name,
                DataType = col.DataType,
                IsNullable = col.IsNullable,
                IsPrimaryKey = col.IsPrimaryKey,
                IsAutoIncrement = DetectAutoIncrement(col),
                NullPercentage = col.IsNullable ? 10 : 0,
                MinValue = 1,
                MaxValue = 1000,
                Decimals = 2,
                DateStart = DateTime.UtcNow.AddYears(-2),
                DateEnd = DateTime.UtcNow,
                SequenceStart = 1,
                SequenceStep = 1
            };

            // 1. Chaves primárias com auto-incremento são desmarcadas (skipped) por padrão
            if (rule.IsAutoIncrement || (rule.IsPrimaryKey && IsIntegerType(col.DataType)))
            {
                rule.IsAutoIncrement = true;
                rule.IsSkipped = true;
                rule.SelectedGenerator = GeneratorType.Sequence;
                rules.Add(rule);
                continue;
            }

            // 2. Chaves Estrangeiras detectadas
            var fkMatch = foreignKeys.FirstOrDefault(fk =>
                string.Equals(fk.SourceTable, table.Name, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(fk.SourceColumn, col.Name, StringComparison.OrdinalIgnoreCase));

            if (fkMatch != null)
            {
                rule.SelectedGenerator = GeneratorType.ForeignKeySample;
                rule.ForeignKeyTargetTable = fkMatch.TargetTable;
                rule.ForeignKeyTargetColumn = fkMatch.TargetColumn;
                rules.Add(rule);
                continue;
            }

            // 3. Inferência por Expressões Regulares no Nome da Coluna
            var inferred = InferGeneratorByName(col.Name, col.DataType);
            if (inferred.HasValue)
            {
                rule.SelectedGenerator = inferred.Value;
                if (rule.SelectedGenerator == GeneratorType.ChoiceList)
                {
                    rule.Choices = new List<string>(StatusChoices);
                }
                else if (rule.SelectedGenerator == GeneratorType.Number)
                {
                    rule.MinValue = 1;
                    rule.MaxValue = 100;
                }
                else if (rule.SelectedGenerator == GeneratorType.Decimal)
                {
                    rule.MinValue = 10;
                    rule.MaxValue = 5000;
                    rule.Decimals = 2;
                }
                rules.Add(rule);
                continue;
            }

            // 4. Inferência por Tipo de Dados (Fallback)
            rule.SelectedGenerator = InferGeneratorByDataType(col.DataType);
            rules.Add(rule);
        }

        return rules;
    }

    private static bool DetectAutoIncrement(ColumnInfo col)
    {
        var dt = col.DataType?.ToLowerInvariant() ?? "";
        var def = col.DefaultValue?.ToLowerInvariant() ?? "";

        return dt.Contains("identity") ||
               dt.Contains("serial") ||
               dt.Contains("auto_increment") ||
               dt.Contains("autoincrement") ||
               def.Contains("nextval") ||
               def.Contains("identity") ||
               def.Contains("autoincrement");
    }

    private static bool IsIntegerType(string? dataType)
    {
        if (string.IsNullOrWhiteSpace(dataType)) return false;
        var dt = dataType.ToLowerInvariant();
        return dt.Contains("int") || dt.Contains("serial");
    }

    private static GeneratorType? InferGeneratorByName(string columnName, string dataType)
    {
        var normalized = Regex.Replace(columnName.Trim(), "([a-z])([A-Z])", "$1 $2")
            .Replace('_', ' ')
            .Replace('-', ' ')
            .ToLowerInvariant();

        // 1. Email & Phone
        if (Regex.IsMatch(normalized, @"\b(email|e mail|mail)\b", RegexOptions.IgnoreCase))
            return GeneratorType.Email;

        if (Regex.IsMatch(normalized, @"\b(phone|telefone|tel|celular|mobile|whatsapp|fone)\b", RegexOptions.IgnoreCase))
            return GeneratorType.Phone;

        // 2. Geography / Location (antes de checagens genéricas de nome)
        if (Regex.IsMatch(normalized, @"\b(zip|cep|postal code|zipcode)\b", RegexOptions.IgnoreCase))
            return GeneratorType.ZipCode;

        if (Regex.IsMatch(normalized, @"\b(city|cidade|municipio)\b", RegexOptions.IgnoreCase))
            return GeneratorType.City;

        if (Regex.IsMatch(normalized, @"\b(country|pais|nacao)\b", RegexOptions.IgnoreCase))
            return GeneratorType.Country;

        if (Regex.IsMatch(normalized, @"\b(address|endereco|rua|logradouro|street)\b", RegexOptions.IgnoreCase))
            return GeneratorType.Address;

        // 3. Temporal (Date / Time)
        if (Regex.IsMatch(normalized, @"\b(date|data|created at|updated at|deleted at|birth|nascimento|timestamp|published at)\b", RegexOptions.IgnoreCase))
            return GeneratorType.DateTime;

        // 4. Financial & Decimal
        if (Regex.IsMatch(normalized, @"\b(price|preco|valor|amount|salario|salary|cost|custo|tax|saldo|balance|total|subtotal)\b", RegexOptions.IgnoreCase))
            return GeneratorType.Decimal;

        // 5. Quantity & Numbers
        if (Regex.IsMatch(normalized, @"\b(age|idade|count|qtd|quantidade|posicao|score|points)\b", RegexOptions.IgnoreCase))
            return GeneratorType.Number;

        // 6. UUID / Identity
        if (Regex.IsMatch(normalized, @"\b(uuid|guid)\b", RegexOptions.IgnoreCase))
            return GeneratorType.Uuid;

        // 7. Status & Enum Choices
        if (Regex.IsMatch(normalized, @"\b(status|situacao|state|estado registro)\b", RegexOptions.IgnoreCase))
            return GeneratorType.ChoiceList;

        // 8. Booleans
        if (Regex.IsMatch(normalized, @"\b(is |has |ativo|active|enabled|habilitado)\b", RegexOptions.IgnoreCase))
            return GeneratorType.Boolean;

        // 9. Names (colocado após geography para que city_name e country_name sejam classificados como City/Country)
        if (Regex.IsMatch(normalized, @"\b(name|nome|full name|first name|last name|primeiro nome|sobrenome|razao social|contato|author|cliente|usuario|user)\b", RegexOptions.IgnoreCase))
            return GeneratorType.Name;

        return null;
    }

    private static GeneratorType InferGeneratorByDataType(string dataType)
    {
        var dt = dataType?.ToLowerInvariant() ?? "";

        if (dt.Contains("bool") || dt.Contains("bit"))
            return GeneratorType.Boolean;

        if (dt.Contains("uuid") || dt.Contains("uniqueidentifier"))
            return GeneratorType.Uuid;

        if (dt.Contains("date") || dt.Contains("time"))
            return GeneratorType.DateTime;

        if (dt.Contains("float") || dt.Contains("double") || dt.Contains("decimal") || dt.Contains("numeric") || dt.Contains("money") || dt.Contains("real"))
            return GeneratorType.Decimal;

        if (dt.Contains("int") || dt.Contains("serial") || dt.Contains("number"))
            return GeneratorType.Number;

        return GeneratorType.Name;
    }

    /// <summary>
    /// Amostra chaves primárias das tabelas-pai para manter integridade referencial.
    /// </summary>
    public async Task SampleForeignKeysAsync(IDatabaseService db, List<ColumnGeneratorRule> rules, CancellationToken ct = default)
    {
        var fkRules = rules
            .Where(r => !r.IsSkipped && r.SelectedGenerator == GeneratorType.ForeignKeySample)
            .Where(r => !string.IsNullOrWhiteSpace(r.ForeignKeyTargetTable) && !string.IsNullOrWhiteSpace(r.ForeignKeyTargetColumn))
            .ToList();

        if (fkRules.Count == 0) return;

        var dialect = db.Config.Type;

        foreach (var rule in fkRules)
        {
            try
            {
                var targetTable = rule.ForeignKeyTargetTable!;
                var targetCol = rule.ForeignKeyTargetColumn!;

                string query = dialect switch
                {
                    DatabaseType.SqlServer =>
                        $"SELECT DISTINCT TOP 1000 {VisualQueryBuilderService.QuoteIdentifier(targetCol, dialect)} FROM {VisualQueryBuilderService.QuoteIdentifier(targetTable, dialect)};",
                    _ =>
                        $"SELECT DISTINCT {VisualQueryBuilderService.QuoteIdentifier(targetCol, dialect)} FROM {VisualQueryBuilderService.QuoteIdentifier(targetTable, dialect)} LIMIT 1000;"
                };

                var queryResult = await db.ExecuteQueryAsync(query, 1000, ct);
                if (queryResult.Success && queryResult.Rows.Count > 0)
                {
                    rule.SampledValues = queryResult.Rows
                        .Where(row => row.Count > 0 && row[0] != null && row[0] != DBNull.Value)
                        .Select(row => row[0]!)
                        .ToList();

                    rule.ParentTableEmpty = rule.SampledValues.Count == 0;
                }
                else
                {
                    rule.SampledValues.Clear();
                    rule.ParentTableEmpty = true;
                }
            }
            catch
            {
                rule.SampledValues.Clear();
                rule.ParentTableEmpty = true;
            }
        }
    }

    /// <summary>
    /// Gera linhas em memória respeitando as regras, limites e nulabilidade de cada coluna.
    /// </summary>
    public List<Dictionary<string, object?>> GenerateRows(TableMockConfig config, int count)
    {
        var rows = new List<Dictionary<string, object?>>(count);
        var activeRules = config.Rules.Where(r => !r.IsSkipped).ToList();

        for (int i = 0; i < count; i++)
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

            foreach (var rule in activeRules)
            {
                // Verifica nulabilidade (exceto para primary keys)
                if (rule.IsNullable && !rule.IsPrimaryKey && rule.NullPercentage > 0)
                {
                    if (Rnd.Next(100) < rule.NullPercentage)
                    {
                        row[rule.ColumnName] = null;
                        continue;
                    }
                }

                row[rule.ColumnName] = GenerateColumnValue(rule, i);
            }

            rows.Add(row);
        }

        return rows;
    }

    private static object? GenerateColumnValue(ColumnGeneratorRule rule, int rowIndex)
    {
        switch (rule.SelectedGenerator)
        {
            case GeneratorType.Name:
                var fn = FirstNames[Rnd.Next(FirstNames.Length)];
                var ln = LastNames[Rnd.Next(LastNames.Length)];
                return $"{fn} {ln}";

            case GeneratorType.Email:
                var eFirst = FirstNames[Rnd.Next(FirstNames.Length)].ToLowerInvariant();
                var eLast = LastNames[Rnd.Next(LastNames.Length)].ToLowerInvariant();
                var domain = Rnd.Next(3) switch
                {
                    0 => "gmail.com",
                    1 => "outlook.com",
                    _ => "runix.dev"
                };
                return $"{eFirst}.{eLast}{Rnd.Next(10, 999)}@{domain}";

            case GeneratorType.Phone:
                var ddd = Rnd.Next(11, 99);
                var num1 = Rnd.Next(91000, 99999);
                var num2 = Rnd.Next(1000, 9999);
                return $"({ddd}) {num1}-{num2}";

            case GeneratorType.Address:
                var prefix = StreetPrefixes[Rnd.Next(StreetPrefixes.Length)];
                var street = StreetNames[Rnd.Next(StreetNames.Length)];
                var num = Rnd.Next(10, 3500);
                return $"{prefix} {street}, {num}";

            case GeneratorType.City:
                return Cities[Rnd.Next(Cities.Length)];

            case GeneratorType.Country:
                return Countries[Rnd.Next(Countries.Length)];

            case GeneratorType.ZipCode:
                return $"{Rnd.Next(10000, 99999):D5}-{Rnd.Next(100, 999):D3}";

            case GeneratorType.DateTime:
                var start = rule.DateStart ?? DateTime.UtcNow.AddYears(-2);
                var end = rule.DateEnd ?? DateTime.UtcNow;
                if (end < start) end = start.AddDays(1);
                var range = (end - start).TotalSeconds;
                var randSeconds = Rnd.NextDouble() * range;
                return start.AddSeconds(randSeconds);

            case GeneratorType.Number:
                var min = rule.MinValue ?? 1;
                var max = rule.MaxValue ?? 1000;
                if (max < min) max = min + 1;
                return Rnd.NextInt64(min, max + 1);

            case GeneratorType.Decimal:
                var dMin = (double)(rule.MinValue ?? 1);
                var dMax = (double)(rule.MaxValue ?? 1000);
                if (dMax < dMin) dMax = dMin + 1;
                var rawVal = dMin + (Rnd.NextDouble() * (dMax - dMin));
                var decimals = rule.Decimals ?? 2;
                return Math.Round((decimal)rawVal, decimals);

            case GeneratorType.Uuid:
                return Guid.NewGuid().ToString();

            case GeneratorType.Boolean:
                return Rnd.Next(2) == 1;

            case GeneratorType.ChoiceList:
                if (rule.Choices.Count > 0)
                {
                    return rule.Choices[Rnd.Next(rule.Choices.Count)];
                }
                return StatusChoices[Rnd.Next(StatusChoices.Length)];

            case GeneratorType.ForeignKeySample:
                if (rule.SampledValues.Count > 0)
                {
                    return rule.SampledValues[Rnd.Next(rule.SampledValues.Count)];
                }
                // Fallback quando a tabela-pai está vazia
                return rule.IsNullable ? null : (rule.MinValue ?? 1);

            case GeneratorType.Sequence:
                return rule.SequenceStart + (rowIndex * rule.SequenceStep);

            case GeneratorType.Null:
                return null;

            case GeneratorType.CustomFormat:
                if (!string.IsNullOrWhiteSpace(rule.CustomFormat))
                {
                    return rule.CustomFormat.Replace("#", Rnd.Next(0, 10).ToString());
                }
                return $"VAL-{Rnd.Next(1000, 9999)}";

            default:
                return $"Sample-{Rnd.Next(100, 999)}";
        }
    }

    /// <summary>
    /// Gera o script SQL correspondente para exportação direta.
    /// </summary>
    public string GenerateSqlScript(TableMockConfig config, List<Dictionary<string, object?>> rows, DatabaseType dialect)
    {
        if (rows.Count == 0) return string.Empty;

        var activeRules = config.Rules.Where(r => !r.IsSkipped).ToList();
        if (activeRules.Count == 0) return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine($"-- RUNIX Mock Data Generator — Tabela: {config.TableName}");
        sb.AppendLine($"-- Dialeto: {dialect} | Total de Linhas: {rows.Count}");
        sb.AppendLine($"-- Gerado em: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine();

        var quotedTable = VisualQueryBuilderService.QuoteIdentifier(config.TableName, dialect);
        var colHeaders = string.Join(", ", activeRules.Select(r => VisualQueryBuilderService.QuoteIdentifier(r.ColumnName, dialect)));

        // Divide em blocos de até 100 linhas por INSERT para legibilidade e compatibilidade
        const int chunkSize = 100;
        for (int i = 0; i < rows.Count; i += chunkSize)
        {
            var chunk = rows.Skip(i).Take(chunkSize).ToList();
            sb.AppendLine($"INSERT INTO {quotedTable} ({colHeaders}) VALUES");

            for (int j = 0; j < chunk.Count; j++)
            {
                var row = chunk[j];
                var valuesList = new List<string>();

                foreach (var rule in activeRules)
                {
                    row.TryGetValue(rule.ColumnName, out var val);
                    valuesList.Add(FormatSqlLiteral(val, dialect));
                }

                var line = $"  ({string.Join(", ", valuesList)})";
                if (j < chunk.Count - 1)
                {
                    line += ",";
                }
                else
                {
                    line += ";";
                }
                sb.AppendLine(line);
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string FormatSqlLiteral(object? value, DatabaseType dialect)
    {
        if (value == null || value == DBNull.Value)
            return "NULL";

        return value switch
        {
            bool b => (dialect == DatabaseType.PostgreSQL) ? (b ? "TRUE" : "FALSE") : (b ? "1" : "0"),
            int or long or short or byte => Convert.ToString(value, CultureInfo.InvariantCulture)!,
            decimal or double or float => Convert.ToString(value, CultureInfo.InvariantCulture)!,
            DateTime dt => $"'{dt:yyyy-MM-dd HH:mm:ss}'",
            Guid g => $"'{g}'",
            _ => $"'{value.ToString()!.Replace("'", "''")}'"
        };
    }

    /// <summary>
    /// Calcula o tamanho seguro do lote de inserção para respeitar os limites de parâmetros de cada engine.
    /// SQL Server: máx 2.100 parâmetros por comando.
    /// SQLite: máx 999 parâmetros por comando.
    /// </summary>
    public static int CalculateSafeBatchSize(int columnCount, DatabaseType dialect)
    {
        if (columnCount <= 0) return 50;

        return dialect switch
        {
            DatabaseType.SqlServer =>
                Math.Clamp(2000 / columnCount, 1, 100),

            DatabaseType.SQLite =>
                Math.Clamp(900 / columnCount, 1, 200),

            DatabaseType.MySQL or DatabaseType.MariaDB =>
                Math.Clamp(3000 / columnCount, 1, 500),

            DatabaseType.PostgreSQL =>
                Math.Clamp(5000 / columnCount, 1, 500),

            _ => Math.Clamp(1000 / columnCount, 1, 100)
        };
    }

    /// <summary>
    /// Insere as linhas geradas no banco de dados de forma atômica e em lotes seguros.
    /// </summary>
    public async Task<MockGenerationResult> SeedBatchAsync(
        IDatabaseService db,
        TableMockConfig config,
        IProgress<MockDataProgress>? progress = null,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var activeRules = config.Rules.Where(r => !r.IsSkipped).ToList();

        if (activeRules.Count == 0)
        {
            return new MockGenerationResult
            {
                Success = false,
                ErrorMessage = "Nenhuma coluna selecionada para geração de dados.",
                ElapsedMilliseconds = 0
            };
        }

        // Verifica integridade referencial antes de inserir
        var emptyFk = activeRules.FirstOrDefault(r => r.SelectedGenerator == GeneratorType.ForeignKeySample && r.ParentTableEmpty && !r.IsNullable);
        if (emptyFk != null)
        {
            return new MockGenerationResult
            {
                Success = false,
                ErrorMessage = $"A tabela de relacionamento '{emptyFk.ForeignKeyTargetTable}' está vazia e a coluna '{emptyFk.ColumnName}' não permite nulos. A inserção violaria a integridade referencial.",
                ElapsedMilliseconds = sw.ElapsedMilliseconds
            };
        }

        var totalRows = config.RowCount;
        var rows = GenerateRows(config, totalRows);
        var dialect = db.Config.Type;
        var batchSize = CalculateSafeBatchSize(activeRules.Count, dialect);

        var batches = new List<List<Dictionary<string, object?>>>();
        for (int i = 0; i < rows.Count; i += batchSize)
        {
            batches.Add(rows.Skip(i).Take(batchSize).ToList());
        }

        var quotedTable = VisualQueryBuilderService.QuoteIdentifier(config.TableName, dialect);
        var colHeaders = string.Join(", ", activeRules.Select(r => VisualQueryBuilderService.QuoteIdentifier(r.ColumnName, dialect)));

        int insertedCount = 0;
        int totalBatches = batches.Count;

        // Monta os comandos parametrizados para cada lote
        var commands = new List<ParameterizedCommand>(totalBatches);

        for (int bIndex = 0; bIndex < totalBatches; bIndex++)
        {
            var chunk = batches[bIndex];
            var sqlSb = new StringBuilder();
            sqlSb.Append($"INSERT INTO {quotedTable} ({colHeaders}) VALUES ");

            var parameters = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            var valueClauses = new List<string>();

            for (int rIdx = 0; rIdx < chunk.Count; rIdx++)
            {
                var row = chunk[rIdx];
                var paramPlaceholders = new List<string>();

                for (int cIdx = 0; cIdx < activeRules.Count; cIdx++)
                {
                    var rule = activeRules[cIdx];
                    var pName = $"p_{bIndex}_{rIdx}_{cIdx}";
                    paramPlaceholders.Add($"@{pName}");
                    row.TryGetValue(rule.ColumnName, out var cellVal);
                    parameters[pName] = cellVal ?? DBNull.Value;
                }

                valueClauses.Add($"({string.Join(", ", paramPlaceholders)})");
            }

            sqlSb.Append(string.Join(", ", valueClauses));
            sqlSb.Append(';');

            commands.Add(new ParameterizedCommand(sqlSb.ToString(), parameters));
        }

        try
        {
            // Executa em transação atômica
            var batchResult = await db.ExecuteBatchTransactionalAsync(commands, ct);
            if (!batchResult.Success)
            {
                return new MockGenerationResult
                {
                    Success = false,
                    ErrorMessage = batchResult.ErrorMessage,
                    ElapsedMilliseconds = sw.ElapsedMilliseconds
                };
            }

            insertedCount = totalRows;
            progress?.Report(new MockDataProgress
            {
                TotalRows = totalRows,
                RowsInserted = insertedCount,
                CurrentBatch = totalBatches,
                TotalBatches = totalBatches,
                Percentage = 100,
                ElapsedMilliseconds = sw.ElapsedMilliseconds
            });

            return new MockGenerationResult
            {
                Success = true,
                RowsInserted = insertedCount,
                ElapsedMilliseconds = sw.ElapsedMilliseconds,
                GeneratedSqlScript = GenerateSqlScript(config, rows, dialect)
            };
        }
        catch (Exception ex)
        {
            return new MockGenerationResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                ElapsedMilliseconds = sw.ElapsedMilliseconds
            };
        }
    }
}
