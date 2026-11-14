using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia.Controls;
using Runix.Core.Interfaces;
using Runix.Core.Models.Dba;
using Runix.Infrastructure.Dba;

namespace Runix.Desktop.Views.Dba;

public partial class PrivilegeMatrixControl : UserControl
{
    private IDatabaseService? _databaseService;
    private readonly List<TablePrivilegeRowViewModel> _allRows = new();
    private readonly List<PostgresPrivilegeChange> _pendingChanges = new();
    private bool _isLoading;

    public event Action<string>? StatusMessageChanged;

    public PrivilegeMatrixControl()
    {
        InitializeComponent();

        ComboRoles.SelectionChanged += async (s, e) =>
        {
            if (!_isLoading) await LoadPrivilegesForSelectedRoleAsync();
        };

        ComboSchemas.SelectionChanged += (s, e) => FilterRows();
        TxtTableFilter.TextChanged += (s, e) => FilterRows();

        BtnRefresh.Click += async (s, e) => await LoadDataAsync();

        BtnDiscardChanges.Click += (s, e) => DiscardChanges();
        BtnReviewAndApply.Click += (s, e) => OpenReviewModal();

        BtnClosePreview.Click += (s, e) => PanelPreviewOverlay.IsVisible = false;
        BtnCancelPreview.Click += (s, e) => PanelPreviewOverlay.IsVisible = false;
        BtnConfirmApply.Click += async (s, e) => await ApplyChangesAsync();
    }

    public void Initialize(IDatabaseService databaseService)
    {
        _databaseService = databaseService;
        _ = LoadDataAsync();
    }

    public async Task LoadDataAsync()
    {
        if (_databaseService == null) return;

        PanelLoading.IsVisible = true;
        _isLoading = true;

        try
        {
            var roles = await PostgresAdminService.Instance.GetRolesAsync(_databaseService);
            var roleNames = roles.Select(r => r.RoleName).ToList();

            ComboRoles.ItemsSource = roleNames;
            if (roleNames.Count > 0 && ComboRoles.SelectedItem == null)
            {
                ComboRoles.SelectedItem = roleNames.FirstOrDefault(r => r != "postgres") ?? roleNames[0];
            }

            // Populate Schemas
            var tables = await _databaseService.GetTablesAsync();
            var schemas = new List<string> { "Todos os Schemas" };
            schemas.AddRange(tables.Select(t => t.Schema).Distinct().OrderBy(s => s));
            ComboSchemas.ItemsSource = schemas;
            ComboSchemas.SelectedIndex = 0;

            _isLoading = false;
            await LoadPrivilegesForSelectedRoleAsync();
        }
        catch (Exception ex)
        {
            StatusMessageChanged?.Invoke($"Erro ao carregar roles: {ex.Message}");
        }
        finally
        {
            _isLoading = false;
            PanelLoading.IsVisible = false;
        }
    }

    public async Task LoadPrivilegesForSelectedRoleAsync()
    {
        if (_databaseService == null || ComboRoles.SelectedItem == null) return;

        var roleName = ComboRoles.SelectedItem.ToString();
        if (string.IsNullOrEmpty(roleName)) return;

        PanelLoading.IsVisible = true;
        _pendingChanges.Clear();
        UpdateStagingBar();

        try
        {
            var privs = await PostgresAdminService.Instance.GetTablePrivilegesAsync(_databaseService, roleName);
            _allRows.Clear();

            foreach (var p in privs)
            {
                var vm = new TablePrivilegeRowViewModel(p);
                vm.PropertyChanged += OnRowPropertyChanged;
                _allRows.Add(vm);
            }

            FilterRows();
        }
        catch (Exception ex)
        {
            StatusMessageChanged?.Invoke($"Erro ao carregar privilégios: {ex.Message}");
        }
        finally
        {
            PanelLoading.IsVisible = false;
        }
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isLoading) return;
        RecalculateChanges();
    }

    private void RecalculateChanges()
    {
        var roleName = ComboRoles.SelectedItem?.ToString() ?? string.Empty;
        _pendingChanges.Clear();

        foreach (var row in _allRows)
        {
            _pendingChanges.AddRange(row.GetChanges(roleName));
        }

        UpdateStagingBar();
    }

    private void UpdateStagingBar()
    {
        var count = _pendingChanges.Count;
        TxtStagingCount.Text = $"{count} alteraç{(count == 1 ? "ão" : "ões")} de privilégios pendente{(count == 1 ? "" : "s")}";
        PanelStagingBar.IsVisible = count > 0;
    }

    private void FilterRows()
    {
        var schemaFilter = ComboSchemas.SelectedItem?.ToString();
        var tableFilter = TxtTableFilter.Text?.Trim().ToLowerInvariant() ?? string.Empty;

        var filtered = _allRows.AsEnumerable();

        if (!string.IsNullOrEmpty(schemaFilter) && schemaFilter != "Todos os Schemas")
        {
            filtered = filtered.Where(r => string.Equals(r.SchemaName, schemaFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrEmpty(tableFilter))
        {
            filtered = filtered.Where(r => r.TableName.ToLowerInvariant().Contains(tableFilter));
        }

        GridPrivileges.ItemsSource = null;
        GridPrivileges.ItemsSource = filtered.ToList();
    }

    private void DiscardChanges()
    {
        _isLoading = true;
        foreach (var row in _allRows)
        {
            row.ResetToOriginal();
        }
        _pendingChanges.Clear();
        _isLoading = false;

        UpdateStagingBar();
        FilterRows();
    }

    private void OpenReviewModal()
    {
        if (_pendingChanges.Count == 0) return;

        var ddl = PostgresAdminService.Instance.GeneratePrivilegeDdl(_pendingChanges);
        TxtPreviewSql.Text = ddl;
        TxtPreviewStatus.Text = string.Empty;
        PanelPreviewOverlay.IsVisible = true;
    }

    private async Task ApplyChangesAsync()
    {
        if (_databaseService == null || _pendingChanges.Count == 0) return;

        BtnConfirmApply.IsEnabled = false;
        TxtPreviewStatus.Text = "Aplicando permissões...";
        TxtPreviewStatus.Foreground = Avalonia.Media.Brush.Parse("#38bdf8");

        try
        {
            var result = await PostgresAdminService.Instance.ApplyPrivilegeChangesAsync(_databaseService, _pendingChanges);
            if (result.Success)
            {
                PanelPreviewOverlay.IsVisible = false;
                StatusMessageChanged?.Invoke("✓ Permissões atualizadas com sucesso!");
                await LoadPrivilegesForSelectedRoleAsync();
            }
            else
            {
                TxtPreviewStatus.Text = $"Erro: {result.ErrorMessage}";
                TxtPreviewStatus.Foreground = Avalonia.Media.Brush.Parse("#f87171");
            }
        }
        catch (Exception ex)
        {
            TxtPreviewStatus.Text = $"Exceção: {ex.Message}";
            TxtPreviewStatus.Foreground = Avalonia.Media.Brush.Parse("#f87171");
        }
        finally
        {
            BtnConfirmApply.IsEnabled = true;
        }
    }

    public void EmulatePrivilegesForTesting(IReadOnlyList<PostgresTablePrivilege> privs)
    {
        _allRows.Clear();
        foreach (var p in privs)
        {
            var vm = new TablePrivilegeRowViewModel(p);
            _allRows.Add(vm);
        }
        FilterRows();
    }
}

public class TablePrivilegeRowViewModel : INotifyPropertyChanged
{
    private readonly PostgresTablePrivilege _original;

    private bool _hasSelect;
    private bool _hasInsert;
    private bool _hasUpdate;
    private bool _hasDelete;
    private bool _hasTruncate;
    private bool _hasReferences;
    private bool _hasTrigger;

    public string SchemaName => _original.SchemaName;
    public string TableName => _original.TableName;

    public bool HasSelect
    {
        get => _hasSelect;
        set => SetField(ref _hasSelect, value);
    }

    public bool HasInsert
    {
        get => _hasInsert;
        set => SetField(ref _hasInsert, value);
    }

    public bool HasUpdate
    {
        get => _hasUpdate;
        set => SetField(ref _hasUpdate, value);
    }

    public bool HasDelete
    {
        get => _hasDelete;
        set => SetField(ref _hasDelete, value);
    }

    public bool HasTruncate
    {
        get => _hasTruncate;
        set => SetField(ref _hasTruncate, value);
    }

    public bool HasReferences
    {
        get => _hasReferences;
        set => SetField(ref _hasReferences, value);
    }

    public bool HasTrigger
    {
        get => _hasTrigger;
        set => SetField(ref _hasTrigger, value);
    }

    public TablePrivilegeRowViewModel(PostgresTablePrivilege original)
    {
        _original = original;
        ResetToOriginal();
    }

    public void ResetToOriginal()
    {
        _hasSelect = _original.HasSelect;
        _hasInsert = _original.HasInsert;
        _hasUpdate = _original.HasUpdate;
        _hasDelete = _original.HasDelete;
        _hasTruncate = _original.HasTruncate;
        _hasReferences = _original.HasReferences;
        _hasTrigger = _original.HasTrigger;

        OnPropertyChanged(nameof(HasSelect));
        OnPropertyChanged(nameof(HasInsert));
        OnPropertyChanged(nameof(HasUpdate));
        OnPropertyChanged(nameof(HasDelete));
        OnPropertyChanged(nameof(HasTruncate));
        OnPropertyChanged(nameof(HasReferences));
        OnPropertyChanged(nameof(HasTrigger));
    }

    public List<PostgresPrivilegeChange> GetChanges(string roleName)
    {
        var changes = new List<PostgresPrivilegeChange>();

        CheckDiff("SELECT", _original.HasSelect, _hasSelect, roleName, changes);
        CheckDiff("INSERT", _original.HasInsert, _hasInsert, roleName, changes);
        CheckDiff("UPDATE", _original.HasUpdate, _hasUpdate, roleName, changes);
        CheckDiff("DELETE", _original.HasDelete, _hasDelete, roleName, changes);
        CheckDiff("TRUNCATE", _original.HasTruncate, _hasTruncate, roleName, changes);
        CheckDiff("REFERENCES", _original.HasReferences, _hasReferences, roleName, changes);
        CheckDiff("TRIGGER", _original.HasTrigger, _hasTrigger, roleName, changes);

        return changes;
    }

    private void CheckDiff(string priv, bool originalVal, bool currentVal, string roleName, List<PostgresPrivilegeChange> list)
    {
        if (originalVal != currentVal)
        {
            list.Add(new PostgresPrivilegeChange
            {
                SchemaName = SchemaName,
                TableName = TableName,
                RoleName = roleName,
                Privilege = priv,
                IsGrant = currentVal
            });
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
