using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Runix.Core.Interfaces;
using Runix.Core.Models;
using Runix.Infrastructure.Security;
using Runix.Infrastructure.Storage;

namespace Runix.Desktop.Views.Dialogs;

public partial class ConnectionBundleDialog : UserControl
{
    private readonly IConnectionBundleService _bundleService;
    private readonly IAppStorageService _appStorage;
    private readonly ObservableCollection<BundleCheckItem> _connectionItems = new();
    private readonly ObservableCollection<BundleCheckItem> _queryItems = new();
    private readonly ObservableCollection<BundleCheckItem> _snippetItems = new();
    private bool _isImportMode;
    private bool _isBundleUnlocked;

    public event Action? CloseRequested;
    public event Action<string>? BundleCompleted;

    public ConnectionBundleDialog() : this(ConnectionBundleService.Instance, AppStorageService.Instance)
    {
    }

    public ConnectionBundleDialog(IConnectionBundleService bundleService, IAppStorageService appStorage)
    {
        InitializeComponent();
        _bundleService = bundleService;
        _appStorage = appStorage;

        ListConnections.ItemsSource = _connectionItems;
        ListQueries.ItemsSource = _queryItems;
        ListSnippets.ItemsSource = _snippetItems;

        SetupEvents();
    }

    private void SetupEvents()
    {
        BtnClose.Click += (s, e) => CloseRequested?.Invoke();
        BtnCancel.Click += (s, e) => CloseRequested?.Invoke();

        BtnConnAll.Click += (s, e) => SetAllChecked(_connectionItems, true);
        BtnConnNone.Click += (s, e) => SetAllChecked(_connectionItems, false);

        BtnQueriesAll.Click += (s, e) => SetAllChecked(_queryItems, true);
        BtnQueriesNone.Click += (s, e) => SetAllChecked(_queryItems, false);

        BtnSnippetsAll.Click += (s, e) => SetAllChecked(_snippetItems, true);
        BtnSnippetsNone.Click += (s, e) => SetAllChecked(_snippetItems, false);

        BtnBrowseRcf.Click += async (s, e) => await BrowseRcfFileAsync();
        BtnUnlockRcf.Click += async (s, e) => await UnlockImportFileAsync();

        TxtImportPassword.KeyDown += async (s, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await UnlockImportFileAsync();
            }
        };

        BtnPrimaryAction.Click += async (s, e) =>
        {
            if (_isImportMode)
            {
                await ExecuteImportSelectedAsync();
            }
            else
            {
                await ExecuteExportSelectedAsync();
            }
        };
    }

    public async Task SetupForExportAsync(IReadOnlyList<ConnectionConfig> connections)
    {
        _isImportMode = false;
        _isBundleUnlocked = true;

        TxtDialogTitle.Text = "📤 Exportar Pacote RUNIX (.rcf)";
        TxtDialogSubtitle.Text = "Marque quais conexões, queries salvas e snippets deseja exportar e defina uma senha para criptografar o arquivo .rcf.";

        PanelImportUnlock.IsVisible = false;
        PanelExportPassword.IsVisible = true;
        PanelBundleSelection.IsEnabled = true;
        PanelBundleSelection.Opacity = 1.0;

        BtnPrimaryAction.Content = "📤 Exportar Arquivo .rcf...";
        BtnPrimaryAction.IsEnabled = true;

        await _appStorage.InitializeAsync();
        var savedQueries = await _appStorage.GetSavedQueriesAsync();
        var customSnippets = await _appStorage.GetCustomSnippetsAsync();

        PopulateLists(connections, savedQueries, customSnippets);
        SetStatus("Marque os itens desejados e defina uma senha para gerar o arquivo .rcf criptografado.", "#8b9cb3");
    }

    public async Task SetupForImportAsync(string? preselectedFilePath = null)
    {
        _isImportMode = true;
        _isBundleUnlocked = false;

        TxtDialogTitle.Text = "📥 Importar Pacote RUNIX (.rcf)";
        TxtDialogSubtitle.Text = "Selecione um arquivo .rcf, informe a senha de proteção e escolha quais conexões, queries e snippets deseja importar.";

        PanelImportUnlock.IsVisible = true;
        PanelExportPassword.IsVisible = false;
        PanelBundleSelection.IsEnabled = false;
        PanelBundleSelection.Opacity = 0.45;

        _connectionItems.Clear();
        _queryItems.Clear();
        _snippetItems.Clear();
        UpdateSectionHeaders();

        BtnPrimaryAction.Content = "📥 Importar Selecionados";
        BtnPrimaryAction.IsEnabled = false;

        if (!string.IsNullOrWhiteSpace(preselectedFilePath))
        {
            TxtImportFilePath.Text = preselectedFilePath;
            SetStatus("Arquivo selecionado! Digite a senha do arquivo .rcf e clique em 'Desbloquear .rcf'.", "#38bdf8");
            TxtImportPassword.Focus();
        }
        else
        {
            await BrowseRcfFileAsync();
        }
    }

    private async Task BrowseRcfFileAsync()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Selecionar Arquivo de Conexões RUNIX (.rcf)",
            AllowMultiple = false,
            FileTypeFilter = new List<FilePickerFileType>
            {
                new("Runix Connections File (*.rcf)") { Patterns = new[] { "*.rcf" } },
                new("Todos os arquivos (*.*)") { Patterns = new[] { "*.*" } }
            }
        });

        if (files.Count > 0)
        {
            TxtImportFilePath.Text = files[0].Path.LocalPath;
            SetStatus("Digite a senha do arquivo .rcf e clique em '🔓 Desbloquear .rcf'.", "#38bdf8");
            TxtImportPassword.Focus();
        }
    }

    public async Task UnlockImportFileAsync()
    {
        string path = (TxtImportFilePath.Text ?? string.Empty).Trim();
        string password = TxtImportPassword.Text ?? string.Empty;

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            SetStatus("✗ Selecione um arquivo .rcf válido.", "#f87171");
            return;
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            SetStatus("✗ Informe a senha definida na criação do arquivo .rcf.", "#f87171");
            return;
        }

        try
        {
            var bundle = await _bundleService.ReadAndDecryptFileAsync(path, password);
            PopulateLists(bundle.Connections, bundle.SavedQueries, bundle.CustomSnippets);

            _isBundleUnlocked = true;
            PanelBundleSelection.IsEnabled = true;
            PanelBundleSelection.Opacity = 1.0;
            BtnPrimaryAction.IsEnabled = true;

            SetStatus(
                $"✓ Arquivo desbloqueado! ({bundle.Connections.Count} conexões, {bundle.SavedQueries.Count} queries, {bundle.CustomSnippets.Count} snippets). Selecione o que deseja importar.",
                "#34d399");
        }
        catch (Exception ex)
        {
            _isBundleUnlocked = false;
            PanelBundleSelection.IsEnabled = false;
            PanelBundleSelection.Opacity = 0.45;
            BtnPrimaryAction.IsEnabled = false;
            SetStatus($"✗ {ex.Message}", "#f87171");
        }
    }

    private async Task ExecuteExportSelectedAsync()
    {
        var selectedConns = _connectionItems
            .Where(x => x.IsSelected && x.Payload is ConnectionConfig)
            .Select(x => ((ConnectionConfig)x.Payload!).Clone())
            .ToList();

        var selectedQueries = _queryItems
            .Where(x => x.IsSelected && x.Payload is SavedQuery)
            .Select(x => (SavedQuery)x.Payload!)
            .ToList();

        var selectedSnippets = _snippetItems
            .Where(x => x.IsSelected && x.Payload is CustomSnippet)
            .Select(x => (CustomSnippet)x.Payload!)
            .ToList();

        if (selectedConns.Count == 0 && selectedQueries.Count == 0 && selectedSnippets.Count == 0)
        {
            SetStatus("✗ Marque pelo menos uma conexão, query salva ou snippet para exportar.", "#f87171");
            return;
        }

        string pwd = TxtExportPassword.Text ?? string.Empty;
        string confirm = TxtExportConfirmPassword.Text ?? string.Empty;

        if (string.IsNullOrWhiteSpace(pwd))
        {
            SetStatus("✗ Defina uma senha para criptografar o arquivo .rcf.", "#f87171");
            TxtExportPassword.Focus();
            return;
        }

        if (!string.Equals(pwd, confirm, StringComparison.Ordinal))
        {
            SetStatus("✗ A confirmação de senha não confere com a senha digitada.", "#f87171");
            TxtExportConfirmPassword.Focus();
            return;
        }

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var saveFile = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Salvar Arquivo de Conexões RUNIX (.rcf)",
            SuggestedFileName = $"runix_bundle_{DateTime.Now:yyyyMMdd}.rcf",
            DefaultExtension = "rcf",
            FileTypeChoices = new List<FilePickerFileType>
            {
                new("Runix Connections File (*.rcf)") { Patterns = new[] { "*.rcf" } }
            }
        });

        if (saveFile == null)
        {
            return;
        }

        try
        {
            var bundle = new RunixConnectionBundle
            {
                ExportedAtUtc = DateTime.UtcNow,
                Connections = selectedConns,
                SavedQueries = selectedQueries,
                CustomSnippets = selectedSnippets
            };

            string targetPath = saveFile.Path.LocalPath;
            await _bundleService.ExportToFileAsync(targetPath, bundle, pwd);

            BundleCompleted?.Invoke(
                $"✓ Pacote exportado com sucesso para '{Path.GetFileName(targetPath)}' ({selectedConns.Count} conexões, {selectedQueries.Count} queries, {selectedSnippets.Count} snippets)!");
        }
        catch (Exception ex)
        {
            SetStatus($"✗ Erro ao exportar arquivo .rcf: {ex.Message}", "#f87171");
        }
    }

    private async Task ExecuteImportSelectedAsync()
    {
        if (!_isBundleUnlocked)
        {
            SetStatus("✗ Desbloqueie primeiro o arquivo .rcf com a senha.", "#f87171");
            return;
        }

        var selectedConns = _connectionItems
            .Where(x => x.IsSelected && x.Payload is ConnectionConfig)
            .Select(x => ((ConnectionConfig)x.Payload!).Clone())
            .ToList();

        var selectedQueries = _queryItems
            .Where(x => x.IsSelected && x.Payload is SavedQuery)
            .Select(x => (SavedQuery)x.Payload!)
            .ToList();

        var selectedSnippets = _snippetItems
            .Where(x => x.IsSelected && x.Payload is CustomSnippet)
            .Select(x => (CustomSnippet)x.Payload!)
            .ToList();

        if (selectedConns.Count == 0 && selectedQueries.Count == 0 && selectedSnippets.Count == 0)
        {
            SetStatus("✗ Selecione pelo menos um item para importar.", "#f87171");
            return;
        }

        try
        {
            // 1. Merge Connections into ConnectionStorageService
            if (selectedConns.Count > 0)
            {
                var existing = ConnectionStorageService.LoadConnections();
                foreach (var incoming in selectedConns)
                {
                    int idx = existing.FindIndex(c =>
                        string.Equals(c.Id, incoming.Id, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(c.Name, incoming.Name, StringComparison.OrdinalIgnoreCase));

                    if (idx >= 0)
                    {
                        existing[idx] = incoming;
                    }
                    else
                    {
                        existing.Add(incoming);
                    }
                }
                ConnectionStorageService.SaveConnections(existing);
            }

            // 2. Merge Saved Queries & Snippets into IAppStorageService
            await _appStorage.InitializeAsync();
            foreach (var q in selectedQueries)
            {
                await _appStorage.SaveQueryAsync(q);
            }

            foreach (var s in selectedSnippets)
            {
                await _appStorage.SaveCustomSnippetAsync(s);
            }

            BundleCompleted?.Invoke(
                $"✓ Importados {selectedConns.Count} conexão(ões), {selectedQueries.Count} query(ies) e {selectedSnippets.Count} snippet(s) do arquivo .rcf!");
        }
        catch (Exception ex)
        {
            SetStatus($"✗ Erro ao importar itens: {ex.Message}", "#f87171");
        }
    }

    private void PopulateLists(
        IReadOnlyList<ConnectionConfig> connections,
        IReadOnlyList<SavedQuery> queries,
        IReadOnlyList<CustomSnippet> snippets)
    {
        _connectionItems.Clear();
        foreach (var conn in connections)
        {
            _connectionItems.Add(new BundleCheckItem
            {
                IsSelected = true,
                Title = conn.Name,
                Subtitle = conn.DisplaySubtitle,
                Badge = conn.Type.ToString(),
                Payload = conn
            });
        }

        _queryItems.Clear();
        foreach (var q in queries)
        {
            _queryItems.Add(new BundleCheckItem
            {
                IsSelected = true,
                Title = q.Title,
                Subtitle = q.SqlText.Replace("\r", " ").Replace("\n", " "),
                Badge = q.IsFavorite ? "★" : "SQL",
                Payload = q
            });
        }

        _snippetItems.Clear();
        foreach (var s in snippets)
        {
            _snippetItems.Add(new BundleCheckItem
            {
                IsSelected = true,
                Title = s.Title,
                Subtitle = s.Body.Replace("\r", " ").Replace("\n", " "),
                Badge = s.Prefix,
                Payload = s
            });
        }

        UpdateSectionHeaders();
    }

    private void UpdateSectionHeaders()
    {
        TxtHeaderConnections.Text = $"🔌 Conexões ({_connectionItems.Count})";
        TxtHeaderQueries.Text = $"⭐ Queries Salvas ({_queryItems.Count})";
        TxtHeaderSnippets.Text = $"✂ Snippets SQL ({_snippetItems.Count})";
    }

    private static void SetAllChecked(IEnumerable<BundleCheckItem> items, bool isChecked)
    {
        foreach (var item in items)
        {
            item.IsSelected = isChecked;
        }
    }

    private void SetStatus(string text, string colorHex)
    {
        TxtStatus.Text = text;
        TxtStatus.Foreground = Brush.Parse(colorHex);
    }
}
