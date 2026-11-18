using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Runix.Core.Constants;
using Runix.Core.Interfaces;
using Runix.Core.Models.Diff;
using Runix.Infrastructure.Diff;
using Runix.Infrastructure.Storage;

namespace Runix.Desktop.Views.Dialogs;

public partial class CaptureSnapshotDialog : UserControl
{
    private readonly IDatabaseService? _databaseService;
    private readonly ISchemaDiffService _diffService;

    public event Action? CloseRequested;
    public event Action<string, SchemaSnapshotEnvelope>? SnapshotCaptured;

    public CaptureSnapshotDialog() : this(null)
    {
    }

    public CaptureSnapshotDialog(IDatabaseService? databaseService)
    {
        InitializeComponent();
        _databaseService = databaseService;
        _diffService = new SchemaDiffService();

        BtnClose.Click += (s, e) => CloseRequested?.Invoke();
        BtnCancel.Click += (s, e) => CloseRequested?.Invoke();
        BtnCapture.Click += async (s, e) => await ExecuteCaptureAsync();
    }

    private async Task ExecuteCaptureAsync()
    {
        if (_databaseService == null)
        {
            ShowError("Nenhuma conexão de banco ativa para capturar o snapshot.");
            return;
        }

        PanelValidation.IsVisible = false;
        BtnCapture.IsEnabled = false;
        BtnCancel.IsEnabled = false;
        PrgProgress.IsVisible = true;
        TxtStatusMessage.Text = "Inspecionando schema do banco e extraindo metadados...";

        try
        {
            var options = new SnapshotExtractionOptions
            {
                IncludeTables = true,
                IncludeForeignKeys = true,
                IncludeIndexes = ChkIndexes.IsChecked ?? true,
                IncludeViews = ChkViews.IsChecked ?? true,
                IncludeTriggers = ChkTriggers.IsChecked ?? true,
                IncludeCheckConstraints = ChkChecks.IsChecked ?? true
            };

            var snapshot = await _diffService.ExtractSnapshotAsync(_databaseService, options);

            var label = TxtSnapshotLabel.Text?.Trim();
            var notes = TxtSnapshotNotes.Text?.Trim();
            if (!string.IsNullOrEmpty(label)) snapshot.Label = label;
            if (!string.IsNullOrEmpty(notes)) snapshot.Notes = notes;

            if (RbCatalog.IsChecked == true)
            {
                TxtStatusMessage.Text = "Compactando e gravando snapshot no catálogo local...";
                var filePath = await SchemaSnapshotCatalogService.Instance.SaveToCatalogAsync(snapshot, label, notes);
                var envelope = await SchemaSnapshotStorageService.Instance.LoadSnapshotAsync(filePath);

                TxtStatusMessage.Text = "✓ Snapshot gravado no catálogo com sucesso!";
                SnapshotCaptured?.Invoke(filePath, envelope);
                CloseRequested?.Invoke();
            }
            else
            {
                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel == null)
                {
                    ShowError("Não foi possível acessar a janela principal para selecionar o arquivo.");
                    ResetUi();
                    return;
                }

                var defaultFileName = $"snapshot_{SanitizeFileName(snapshot.DatabaseName)}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.rschema";

                var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Salvar Snapshot de Schema (.rschema)",
                    DefaultExtension = "rschema",
                    SuggestedFileName = defaultFileName,
                    FileTypeChoices = new List<FilePickerFileType>
                    {
                        new("RUNIX Schema Snapshot (*.rschema)")
                        {
                            Patterns = new[] { "*.rschema" }
                        },
                        new("JSON Schema Snapshot (*.json)")
                        {
                            Patterns = new[] { "*.json" }
                        }
                    }
                });

                if (file == null)
                {
                    ResetUi();
                    return;
                }

                var destinationPath = file.Path.LocalPath;
                TxtStatusMessage.Text = "Compactando com GZip e gravando arquivo...";

                var metadata = new SnapshotMetadata
                {
                    DatabaseName = snapshot.DatabaseName,
                    EngineType = snapshot.EngineType,
                    TableCount = snapshot.Tables.Count,
                    ViewCount = snapshot.Views.Count,
                    TriggerCount = snapshot.Triggers.Count,
                    Label = label,
                    Notes = notes,
                    Environment = _databaseService?.Config.IsProductionEnvironment == true ? "Production" : "Development"
                };

                await SchemaSnapshotStorageService.Instance.SaveSnapshotAsync(
                    destinationPath,
                    snapshot,
                    metadata,
                    compressGzip: true);

                var envelope = new SchemaSnapshotEnvelope
                {
                    FormatVersion = 1,
                    AppVersion = AppConstants.AppVersion,
                    ChecksumSha256 = SchemaSnapshotStorageService.Instance.ComputeChecksum(snapshot),
                    Metadata = metadata,
                    Snapshot = snapshot
                };

                TxtStatusMessage.Text = "✓ Snapshot gravado com sucesso!";
                SnapshotCaptured?.Invoke(destinationPath, envelope);
                CloseRequested?.Invoke();
            }
        }
        catch (Exception ex)
        {
            ShowError($"Falha na captura do snapshot: {ex.Message}");
            ResetUi();
        }
    }

    private void ShowError(string message)
    {
        TxtValidationMessage.Text = message;
        PanelValidation.IsVisible = true;
    }

    private void ResetUi()
    {
        BtnCapture.IsEnabled = true;
        BtnCancel.IsEnabled = true;
        PrgProgress.IsVisible = false;
    }

    private static string SanitizeFileName(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "database";
        var invalidChars = Path.GetInvalidFileNameChars();
        var chars = input.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (Array.IndexOf(invalidChars, chars[i]) >= 0 || char.IsWhiteSpace(chars[i]))
            {
                chars[i] = '_';
            }
        }
        return new string(chars);
    }
}
