using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;
using Runix.Core.Interfaces;
using Runix.Core.Models.Dba;
using Runix.Infrastructure.Dba;

namespace Runix.Desktop.Views.Dba;

public partial class PostgresAdminTabControl : UserControl, IDisposable
{
    private readonly IDatabaseService _databaseService;
    private readonly List<PostgresRoleInfo> _allRoles = new();
    private PostgresRoleInfo? _selectedRole;
    private readonly RoleEditorDialog _roleEditor = new();
    private bool _isEditingExisting;

    public PostgresAdminTabControl() : this(new Runix.Infrastructure.Data.SqliteDatabaseService(new Runix.Core.Models.ConnectionConfig()))
    {
    }

    public PostgresAdminTabControl(IDatabaseService databaseService)
    {
        _databaseService = databaseService;
        InitializeComponent();

        SetupEvents();
        SetupRoleEditor();

        ViewPrivilegeMatrix.StatusMessageChanged += msg => SetStatus(msg);
        ViewActiveSessions.StatusMessageChanged += msg => SetStatus(msg);

        // Initialize sub-components
        ViewPrivilegeMatrix.Initialize(_databaseService);
        ViewActiveSessions.Initialize(_databaseService);

        _ = LoadRolesAsync();
    }

    private void SetupEvents()
    {
        BtnSubTabRoles.Click += (s, e) => SwitchSubTab(0);
        BtnSubTabPrivileges.Click += (s, e) => SwitchSubTab(1);
        BtnSubTabSessions.Click += (s, e) => SwitchSubTab(2);

        BtnNewRole.Click += (s, e) => OpenCreateRoleModal();
        BtnEditRole.Click += (s, e) => OpenEditRoleModal();
        BtnDeleteRole.Click += (s, e) => OpenDeleteRoleModal();
        BtnRefreshRoles.Click += async (s, e) => await LoadRolesAsync();

        TxtRoleFilter.TextChanged += (s, e) => FilterRoles();
        GridRoles.SelectionChanged += OnGridRolesSelectionChanged;

        BtnCancelDelete.Click += (s, e) => CloseModal();
        BtnConfirmDelete.Click += async (s, e) => await ExecuteDeleteRoleAsync();
    }

    private void SetupRoleEditor()
    {
        _roleEditor.CancelRequested += CloseModal;
        _roleEditor.SaveRequested += async (role, password) => await OnRoleSaveRequestedAsync(role, password);
    }

    public void SwitchSubTab(int index)
    {
        PanelSubTabRoles.IsVisible = index == 0;
        ViewPrivilegeMatrix.IsVisible = index == 1;
        ViewActiveSessions.IsVisible = index == 2;

        BtnSubTabRoles.Classes.Clear();
        BtnSubTabPrivileges.Classes.Clear();
        BtnSubTabSessions.Classes.Clear();

        BtnSubTabRoles.Classes.Add(index == 0 ? "btn-secondary" : "btn-outline");
        BtnSubTabPrivileges.Classes.Add(index == 1 ? "btn-secondary" : "btn-outline");
        BtnSubTabSessions.Classes.Add(index == 2 ? "btn-secondary" : "btn-outline");

        if (index == 1)
        {
            _ = ViewPrivilegeMatrix.LoadDataAsync();
        }
        else if (index == 2)
        {
            _ = ViewActiveSessions.LoadSessionsAsync(isBackgroundPoll: false);
        }
    }

    public async Task LoadRolesAsync()
    {
        PanelLoadingRoles.IsVisible = true;
        SetStatus("Consultando roles do cluster...");

        try
        {
            var roles = await PostgresAdminService.Instance.GetRolesAsync(_databaseService);
            _allRoles.Clear();
            _allRoles.AddRange(roles);

            FilterRoles();
            SetStatus($"✓ {_allRoles.Count} roles carregadas.");
        }
        catch (Exception ex)
        {
            SetStatus($"Erro ao carregar roles: {ex.Message}", isError: true);
        }
        finally
        {
            PanelLoadingRoles.IsVisible = false;
        }
    }

    private void FilterRoles()
    {
        var filter = TxtRoleFilter.Text?.Trim().ToLowerInvariant() ?? string.Empty;
        var filtered = _allRoles.AsEnumerable();

        if (!string.IsNullOrEmpty(filter))
        {
            filtered = filtered.Where(r => r.RoleName.ToLowerInvariant().Contains(filter));
        }

        var list = filtered.ToList();
        GridRoles.ItemsSource = null;
        GridRoles.ItemsSource = list;

        TxtRoleCount.Text = $"{list.Count} roles";

        if (_selectedRole != null)
        {
            var match = list.FirstOrDefault(r => r.RoleName == _selectedRole.RoleName);
            UpdateRoleSelection(match);
        }
        else
        {
            UpdateRoleSelection(null);
        }
    }

    private void OnGridRolesSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var selected = GridRoles.SelectedItem as PostgresRoleInfo;
        UpdateRoleSelection(selected);
    }

    private void UpdateRoleSelection(PostgresRoleInfo? role)
    {
        _selectedRole = role;
        var hasSel = role != null;

        BtnEditRole.IsEnabled = hasSel;
        // Postgres superuser or default roles shouldn't be casually deleted
        BtnDeleteRole.IsEnabled = hasSel && !string.Equals(role?.RoleName, "postgres", StringComparison.OrdinalIgnoreCase);
    }

    private void OpenCreateRoleModal()
    {
        _isEditingExisting = false;
        _roleEditor.SetupForNewRole();
        ModalContainer.Content = _roleEditor;
        PanelDeleteConfirm.IsVisible = false;
        PanelModalOverlay.IsVisible = true;
    }

    private void OpenEditRoleModal()
    {
        if (_selectedRole == null) return;

        _isEditingExisting = true;
        _roleEditor.SetupForEditRole(_selectedRole);
        ModalContainer.Content = _roleEditor;
        PanelDeleteConfirm.IsVisible = false;
        PanelModalOverlay.IsVisible = true;
    }

    private void OpenDeleteRoleModal()
    {
        if (_selectedRole == null) return;

        TxtDeleteMessage.Text = $"Tem certeza que deseja excluir permanentemente a role '{_selectedRole.RoleName}'? Todos os privilégios e permissões associados serão removidos do cluster.";
        ModalContainer.Content = null;
        PanelDeleteConfirm.IsVisible = true;
        PanelModalOverlay.IsVisible = true;
    }

    private void CloseModal()
    {
        PanelModalOverlay.IsVisible = false;
        ModalContainer.Content = null;
        PanelDeleteConfirm.IsVisible = false;
    }

    private async Task OnRoleSaveRequestedAsync(PostgresRoleInfo role, string? password)
    {
        SetStatus(_isEditingExisting ? $"Atualizando role {role.RoleName}..." : $"Criando role {role.RoleName}...");

        try
        {
            var result = _isEditingExisting
                ? await PostgresAdminService.Instance.AlterRoleAsync(_databaseService, role, password)
                : await PostgresAdminService.Instance.CreateRoleAsync(_databaseService, role, password);

            if (result.Success)
            {
                CloseModal();
                SetStatus(_isEditingExisting
                    ? $"✓ Role '{role.RoleName}' atualizada com sucesso!"
                    : $"✓ Role '{role.RoleName}' criada com sucesso!");
                await LoadRolesAsync();
            }
            else
            {
                SetStatus($"Falha ao salvar role: {result.ErrorMessage}", isError: true);
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Exceção: {ex.Message}", isError: true);
        }
    }

    private async Task ExecuteDeleteRoleAsync()
    {
        if (_selectedRole == null) return;

        var roleName = _selectedRole.RoleName;
        CloseModal();
        SetStatus($"Excluindo role '{roleName}'...");

        try
        {
            var result = await PostgresAdminService.Instance.DropRoleAsync(_databaseService, roleName);
            if (result.Success)
            {
                SetStatus($"✓ Role '{roleName}' excluída com sucesso!");
                await LoadRolesAsync();
            }
            else
            {
                SetStatus($"Falha ao excluir role: {result.ErrorMessage}", isError: true);
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Exceção: {ex.Message}", isError: true);
        }
    }

    public void SetStatus(string message, bool isError = false)
    {
        TxtStatusMessage.Text = message;
        TxtStatusMessage.Foreground = Brush.Parse(isError ? "#f87171" : "#34d399");
    }

    /// <summary>
    /// Popula dados sintéticos realistas para testes de renderização visual headless off-screen.
    /// </summary>
    public void EmulateDbaForTesting()
    {
        _allRoles.Clear();
        _allRoles.Add(new PostgresRoleInfo
        {
            RoleName = "postgres",
            IsSuperuser = true,
            CanLogin = true,
            CanCreateDb = true,
            CanCreateRole = true,
            InheritsPrivileges = true,
            ValidUntil = null
        });
        _allRoles.Add(new PostgresRoleInfo
        {
            RoleName = "app_backend",
            IsSuperuser = false,
            CanLogin = true,
            CanCreateDb = false,
            CanCreateRole = false,
            InheritsPrivileges = true,
            ValidUntil = DateTime.UtcNow.AddYears(1)
        });
        _allRoles.Add(new PostgresRoleInfo
        {
            RoleName = "analyst_readonly",
            IsSuperuser = false,
            CanLogin = true,
            CanCreateDb = false,
            CanCreateRole = false,
            InheritsPrivileges = true,
            ValidUntil = DateTime.UtcNow.AddMonths(6)
        });
        _allRoles.Add(new PostgresRoleInfo
        {
            RoleName = "replication_user",
            IsSuperuser = false,
            CanLogin = true,
            IsReplication = true,
            ValidUntil = null
        });

        FilterRoles();
        if (_allRoles.Count > 1)
        {
            GridRoles.SelectedItem = _allRoles[1];
        }

        ViewPrivilegeMatrix.EmulatePrivilegesForTesting(new List<PostgresTablePrivilege>
        {
            new() { SchemaName = "public", TableName = "cheeses", RoleName = "app_backend", HasSelect = true, HasInsert = true, HasUpdate = true, HasDelete = true },
            new() { SchemaName = "public", TableName = "orders", RoleName = "app_backend", HasSelect = true, HasInsert = true, HasUpdate = false, HasDelete = false },
            new() { SchemaName = "public", TableName = "customers", RoleName = "app_backend", HasSelect = true, HasInsert = false, HasUpdate = false, HasDelete = false }
        });

        ViewActiveSessions.EmulateSessionsForTesting(new List<PostgresActiveSession>
        {
            new() { Pid = 1204, DatabaseName = "runix_prod", UserName = "app_backend", ApplicationName = "Runix Studio", ClientAddress = "127.0.0.1", State = "active", ElapsedSeconds = 1.2, QueryText = "SELECT * FROM cheeses ORDER BY rating DESC;", IsCurrentSession = true },
            new() { Pid = 1180, DatabaseName = "runix_prod", UserName = "etl_worker", ApplicationName = "worker_node_1", ClientAddress = "10.0.0.15", State = "active", ElapsedSeconds = 8.4, QueryText = "UPDATE orders SET status = 'processed' WHERE created_at < NOW() - INTERVAL '1 day';" },
            new() { Pid = 1102, DatabaseName = "runix_prod", UserName = "analyst", ApplicationName = "pgAdmin", ClientAddress = "192.168.1.50", State = "idle in transaction", ElapsedSeconds = 42.1, QueryText = "BEGIN; SELECT COUNT(*) FROM analytics_events;" }
        });

        SetStatus("✓ Painel de Administração PostgreSQL carregado (4 roles, conexões ativas monitoradas).");
    }

    public void Dispose()
    {
        GridRoles.ItemsSource = null;
        _allRoles.Clear();
    }
}
