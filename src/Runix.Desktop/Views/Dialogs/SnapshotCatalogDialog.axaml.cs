using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Runix.Core.Interfaces;
using Runix.Core.Models.Diff;
using Runix.Infrastructure.Storage;

namespace Runix.Desktop.Views.Dialogs;

public partial class SnapshotCatalogDialog : UserControl
{
    private readonly ISchemaSnapshotCatalogService _catalogService;
    private List<SnapshotCatalogItem> _allItems = new();
    private SnapshotCatalogItem? _pendingRenameItem;
    private SnapshotCatalogItem? _pendingDeleteItem;

    public event Action? CloseRequested;
    public event Action? NewSnapshotRequested;
    public event Action<SnapshotCatalogItem>? CompareRequested;

    public SnapshotCatalogDialog() : this(SchemaSnapshotCatalogService.Instance)
    {
    }

    public SnapshotCatalogDialog(ISchemaSnapshotCatalogService catalogService)
    {
        InitializeComponent();
        _catalogService = catalogService;

        BtnClose.Click += (s, e) => CloseRequested?.Invoke();
        BtnFooterClose.Click += (s, e) => CloseRequested?.Invoke();
        BtnNewSnapshot.Click += (s, e) => NewSnapshotRequested?.Invoke();

        TxtSearch.TextChanged += async (s, e) =>
        {
            await FilterOrSearchAsync(TxtSearch.Text);
        };

        ListSnapshots.DoubleTapped += (s, e) =>
        {
            if (ListSnapshots.SelectedItem is SnapshotCatalogItem selected)
            {
                CompareRequested?.Invoke(selected);
            }
        };

        // Rename modal buttons
        BtnCancelRename.Click += (s, e) => PanelRenameModal.IsVisible = false;
        BtnConfirmRename.Click += async (s, e) => await ExecuteConfirmRenameAsync();

        // Delete modal buttons
        BtnCancelDelete.Click += (s, e) => PanelDeleteModal.IsVisible = false;
        BtnConfirmDelete.Click += async (s, e) => await ExecuteConfirmDeleteAsync();

        _ = RefreshCatalogAsync();
    }

    public async Task RefreshCatalogAsync(string? query = null)
    {
        try
        {
            var items = await _catalogService.GetAllAsync(query);
            _allItems = items.ToList();
            ListSnapshots.ItemsSource = _allItems;

            bool isEmpty = _allItems.Count == 0;
            PanelEmptyState.IsVisible = isEmpty;
            ListSnapshots.IsVisible = !isEmpty;

            TxtCatalogStats.Text = $"{_allItems.Count} snapshot(s) encontrado(s)";
        }
        catch (Exception ex)
        {
            TxtCatalogStats.Text = $"Erro ao carregar catálogo: {ex.Message}";
        }
    }

    private async Task FilterOrSearchAsync(string? query)
    {
        await RefreshCatalogAsync(query);
    }

    private void OnCardCompareClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is SnapshotCatalogItem item)
        {
            CompareRequested?.Invoke(item);
        }
    }

    private async void OnCardExportClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not SnapshotCatalogItem item) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var ext = Path.GetExtension(item.FilePath);
        if (string.IsNullOrEmpty(ext)) ext = ".rschema";

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Exportar Snapshot para Arquivo",
            DefaultExtension = ext.TrimStart('.'),
            SuggestedFileName = item.FileName,
            FileTypeChoices = new List<FilePickerFileType>
            {
                new("RUNIX Schema Snapshot (*.rschema)") { Patterns = new[] { "*.rschema" } },
                new("JSON Snapshot (*.json)") { Patterns = new[] { "*.json" } }
            }
        });

        if (file != null)
        {
            try
            {
                await _catalogService.ExportToFileAsync(item.FilePath, file.Path.LocalPath);
                TxtCatalogStats.Text = $"✓ Exportado com sucesso para {file.Name}";
            }
            catch (Exception ex)
            {
                TxtCatalogStats.Text = $"Erro ao exportar snapshot: {ex.Message}";
            }
        }
    }

    private void OnCardRenameClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not SnapshotCatalogItem item) return;

        _pendingRenameItem = item;
        TxtRenameInput.Text = item.Label;
        PanelRenameModal.IsVisible = true;
        TxtRenameInput.Focus();
    }

    private async Task ExecuteConfirmRenameAsync()
    {
        if (_pendingRenameItem == null) return;

        var newLabel = TxtRenameInput.Text?.Trim();
        if (string.IsNullOrWhiteSpace(newLabel)) return;

        try
        {
            await _catalogService.RenameAsync(_pendingRenameItem.FilePath, newLabel);
            PanelRenameModal.IsVisible = false;
            _pendingRenameItem = null;
            await RefreshCatalogAsync(TxtSearch.Text);
        }
        catch (Exception ex)
        {
            TxtCatalogStats.Text = $"Erro ao renomear: {ex.Message}";
        }
    }

    private void OnCardDeleteClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not SnapshotCatalogItem item) return;

        _pendingDeleteItem = item;
        TxtDeletePrompt.Text = $"Deseja realmente excluir o snapshot '{item.Label}' ({item.DatabaseName})? Esta ação não pode ser desfeita.";
        PanelDeleteModal.IsVisible = true;
    }

    private async Task ExecuteConfirmDeleteAsync()
    {
        if (_pendingDeleteItem == null) return;

        try
        {
            await _catalogService.DeleteAsync(_pendingDeleteItem.FilePath);
            PanelDeleteModal.IsVisible = false;
            _pendingDeleteItem = null;
            await RefreshCatalogAsync(TxtSearch.Text);
        }
        catch (Exception ex)
        {
            TxtCatalogStats.Text = $"Erro ao excluir: {ex.Message}";
        }
    }
}
