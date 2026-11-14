using System;
using System.IO;
using System.Xml;
using AvaloniaEdit;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;
using Avalonia.Media;

namespace Runix.Desktop.Services;

public static class SqlSyntaxHighlighting
{
    private static IHighlightingDefinition? _definition;
    private static readonly object _lock = new();

    public static IHighlightingDefinition GetSqlDarkDefinition()
    {
        if (_definition != null) return _definition;

        lock (_lock)
        {
            if (_definition != null) return _definition;

            try
            {
                var asm = typeof(SqlSyntaxHighlighting).Assembly;
                using var stream = asm.GetManifestResourceStream("Runix.Desktop.Resources.RunixSqlDark.xshd");
                if (stream != null)
                {
                    using var reader = new XmlTextReader(stream);
                    _definition = HighlightingLoader.Load(reader, HighlightingManager.Instance);
                    HighlightingManager.Instance.RegisterHighlighting("RUNIX-SQL", new[] { ".sql" }, _definition);
                    return _definition;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load custom SQL syntax definition: {ex}");
            }

            // Fallback to built-in TSQL definition
            _definition = HighlightingManager.Instance.GetDefinition("TSQL") 
                          ?? HighlightingManager.Instance.GetDefinitionByExtension(".sql");
            return _definition;
        }
    }

    public static void ConfigureSqlEditor(TextEditor editor)
    {
        editor.ShowLineNumbers = true;
        editor.FontFamily = new FontFamily("Consolas,Menlo,Monaco,Courier New,monospace");
        editor.FontSize = 13;
        editor.Background = Brush.Parse("#090d14");
        editor.Foreground = Brush.Parse("#f0f6fc");
        editor.LineNumbersForeground = Brush.Parse("#475569");
        editor.HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
        editor.VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;

        editor.SyntaxHighlighting = GetSqlDarkDefinition();
    }
}
