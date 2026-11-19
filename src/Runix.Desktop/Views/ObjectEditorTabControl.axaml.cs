using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Runix.Core.Interfaces;
using Runix.Core.Models;
using Runix.Core.Services;
using Runix.Desktop.Services;
using Runix.Infrastructure.Data;
using Runix.Infrastructure.Database;

namespace Runix.Desktop.Views;

public partial class ObjectEditorTabControl : UserControl, IDisposable
{
    private readonly IDatabaseService _databaseService;
    private readonly IProgrammabilityService _programmabilityService;
    private readonly ISqlFormatterService _formatterService = new SqlFormatterService();
    private DatabaseObjectInfo _objectInfo;
    private bool _isNewObject;

    public event Action? ObjectsChanged;

    public ObjectEditorTabControl()
        : this(new SqliteDatabaseService(new ConnectionConfig()), new DatabaseObjectInfo { Name = "new_view", ObjectType = DatabaseObjectType.View }, isNewObject: true)
    {
    }

    public ObjectEditorTabControl(IDatabaseService databaseService, DatabaseObjectInfo objectInfo, bool isNewObject = false)
    {
        InitializeComponent();
        _databaseService = databaseService;
        _programmabilityService = new ProgrammabilityService(databaseService);
        _objectInfo = objectInfo;
        _isNewObject = isNewObject;

        SqlSyntaxHighlighting.ConfigureSqlEditor(TxtDdlEditor);
        SetupUi();
        SetupEvents();
        _ = InitializeDdlAsync();
    }

    private void SetupUi()
    {
        TxtTypeBadge.Text = _objectInfo.TypeBadge;
        TxtObjectTitle.Text = _isNewObject ? $"Novo {_objectInfo.ObjectType}" : _objectInfo.QualifiedName;
        TxtObjectSubtitle.Text = _objectInfo.Subtitle;
        TxtDialectBadge.Text = $"Dialeto: {_databaseService.Config.Type}";

        PanelTemplateSelector.IsVisible = _isNewObject;
        BtnDropObject.IsVisible = !_isNewObject;
        BtnPreviewData.IsVisible = _objectInfo.ObjectType == DatabaseObjectType.View;

        if (_isNewObject)
        {
            CmbObjectType.SelectedIndex = _objectInfo.ObjectType switch
            {
                DatabaseObjectType.View => 0,
                DatabaseObjectType.Trigger => 1,
                DatabaseObjectType.Index => 2,
                DatabaseObjectType.Function => 3,
                DatabaseObjectType.Procedure => 4,
                _ => 0
            };
        }
    }

    private void SetupEvents()
    {
        BtnApplyDdl.Click += async (s, e) => await ApplyDdlAsync();
        BtnPreviewData.Click += async (s, e) => await PreviewViewDataAsync();
        BtnFormatDdl.Click += (s, e) => FormatDdlInEditor();
        BtnReloadDdl.Click += async (s, e) => await InitializeDdlAsync();
        BtnDropObject.Click += async (s, e) => await DropObjectAsync();

        BtnCopyDdl.Click += async (s, e) =>
        {
            var top = TopLevel.GetTopLevel(this);
            if (top?.Clipboard != null && !string.IsNullOrEmpty(TxtDdlEditor.Text))
            {
                await top.Clipboard.SetTextAsync(TxtDdlEditor.Text);
                ShowDiagnostic("✓ Definição DDL copiada para a área de transferência.", isError: false);
            }
        };

        CmbObjectType.SelectionChanged += (s, e) =>
        {
            if (!_isNewObject) return;
            _objectInfo.ObjectType = CmbObjectType.SelectedIndex switch
            {
                0 => DatabaseObjectType.View,
                1 => DatabaseObjectType.Trigger,
                2 => DatabaseObjectType.Index,
                3 => DatabaseObjectType.Function,
                4 => DatabaseObjectType.Procedure,
                _ => DatabaseObjectType.View
            };
            TxtTypeBadge.Text = _objectInfo.TypeBadge;
            TxtObjectTitle.Text = $"Novo {_objectInfo.ObjectType}";
            BtnPreviewData.IsVisible = _objectInfo.ObjectType == DatabaseObjectType.View;
            TxtDdlEditor.Text = _programmabilityService.GenerateTemplateDdl(
                _objectInfo.ObjectType,
                $"new_{_objectInfo.ObjectType.ToString().ToLowerInvariant()}",
                _objectInfo.TargetTableName);
        };

        KeyDown += async (s, e) =>
        {
            if ((e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta)) && e.Key == Key.S)
            {
                e.Handled = true;
                await ApplyDdlAsync();
            }
            else if ((e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta)) &&
                     e.KeyModifiers.HasFlag(KeyModifiers.Shift) && e.Key == Key.F)
            {
                e.Handled = true;
                FormatDdlInEditor();
            }
        };
    }

    public void FormatDdlInEditor()
    {
        if (string.IsNullOrWhiteSpace(TxtDdlEditor.Text)) return;
        TxtDdlEditor.Text = _formatterService.Format(TxtDdlEditor.Text);
        TxtFooterStatus.Text = "✨ DDL formatado com sucesso.";
    }

    private async Task InitializeDdlAsync()
    {
        try
        {
            if (_isNewObject)
            {
                TxtDdlEditor.Text = _programmabilityService.GenerateTemplateDdl(
                    _objectInfo.ObjectType,
                    string.IsNullOrWhiteSpace(_objectInfo.Name) ? $"new_{_objectInfo.ObjectType.ToString().ToLowerInvariant()}" : _objectInfo.Name,
                    _objectInfo.TargetTableName);
                TxtFooterStatus.Text = "Template DDL carregado — edite e clique em Aplicar DDL.";
            }
            else
            {
                var ddl = await _programmabilityService.GetObjectDdlAsync(_objectInfo);
                TxtDdlEditor.Text = ddl;
                TxtFooterStatus.Text = $"DDL carregado ({ddl.Length} caracteres)";
            }
        }
        catch (Exception ex)
        {
            ShowDiagnostic($"Erro ao carregar DDL: {ex.Message}", isError: true);
        }
    }

    public async Task ApplyDdlAsync()
    {
        var ddl = TxtDdlEditor.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(ddl))
        {
            ShowDiagnostic("A definição DDL está vazia.", isError: true);
            return;
        }

        BtnApplyDdl.IsEnabled = false;
        TxtFooterStatus.Text = "Aplicando DDL transacionalmente...";

        try
        {
            var result = await _programmabilityService.ApplyObjectDdlAsync(_objectInfo, ddl, isExistingObject: !_isNewObject);
            if (result.Success)
            {
                _isNewObject = false;
                PanelTemplateSelector.IsVisible = false;
                BtnDropObject.IsVisible = true;
                ShowDiagnostic($"✓ Objeto '{_objectInfo.QualifiedName}' aplicado com sucesso em {result.ElapsedMilliseconds} ms!", isError: false);
                TxtFooterStatus.Text = $"Aplicado com sucesso ({result.ElapsedMilliseconds} ms)";
                ObjectsChanged?.Invoke();
            }
            else
            {
                ShowDiagnostic($"✖ Falha ao aplicar DDL (Transação revertida automaticamente via Rollback):\n{result.ErrorMessage}", isError: true);
                TxtFooterStatus.Text = "Erro de sintaxe/execução — Rollback executado";
            }
        }
        catch (Exception ex)
        {
            ShowDiagnostic($"✖ Erro inesperado: {ex.Message}", isError: true);
        }
        finally
        {
            BtnApplyDdl.IsEnabled = true;
        }
    }

    public async Task PreviewViewDataAsync()
    {
        if (_objectInfo.ObjectType != DatabaseObjectType.View || string.IsNullOrWhiteSpace(_objectInfo.Name))
            return;

        TxtFooterStatus.Text = $"Consultando preview de {_objectInfo.QualifiedName}...";
        var sql = $"SELECT * FROM \"{_objectInfo.Name.Replace("\"", "\"\"")}\" LIMIT 100;";
        var res = await _databaseService.ExecuteQueryAsync(sql, 100);

        if (!res.Success)
        {
            ShowDiagnostic($"✖ Erro ao consultar View: {res.ErrorMessage}", isError: true);
            return;
        }

        GridPreview.Columns.Clear();
        for (int i = 0; i < res.Columns.Count; i++)
        {
            var colIndex = i;
            GridPreview.Columns.Add(new DataGridTextColumn
            {
                Header = res.Columns[i],
                Binding = new Binding($"[{colIndex}]")
            });
        }

        GridPreview.ItemsSource = res.Rows;
        BorderDiagnosticMessage.IsVisible = false;
        GridPreview.IsVisible = true;
        TxtPreviewHeader.Text = $"📊 Preview da View ({res.Rows.Count} linhas em {res.ElapsedMilliseconds} ms)";
        TxtFooterStatus.Text = $"{res.Rows.Count} linhas carregadas em {res.ElapsedMilliseconds} ms";
    }

    private async Task DropObjectAsync()
    {
        if (_isNewObject) return;

        var res = await _programmabilityService.DropObjectAsync(_objectInfo);
        if (res.Success)
        {
            ShowDiagnostic($"✓ Objeto '{_objectInfo.QualifiedName}' removido do banco de dados.", isError: false);
            TxtFooterStatus.Text = "Objeto excluído";
            BtnDropObject.IsEnabled = false;
            ObjectsChanged?.Invoke();
        }
        else
        {
            ShowDiagnostic($"✖ Erro ao excluir objeto: {res.ErrorMessage}", isError: true);
        }
    }

    private void ShowDiagnostic(string message, bool isError)
    {
        GridPreview.IsVisible = false;
        BorderDiagnosticMessage.IsVisible = true;
        TxtDiagnosticMessage.Text = message;
        TxtDiagnosticMessage.Foreground = Brush.Parse(isError ? "#f87171" : "#34d399");
        TxtPreviewHeader.Text = isError ? "⚠️ Diagnóstico de Erro (Rollback Atômico)" : "✓ Operação Concluída";
    }

    public void Dispose()
    {
        GridPreview.ItemsSource = null;
        GridPreview.Columns.Clear();
        if (TxtDdlEditor?.Document?.UndoStack != null)
        {
            TxtDdlEditor.Document.UndoStack.ClearAll();
        }
    }
}
