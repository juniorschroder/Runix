using System;
using Avalonia.Controls;
using Runix.Core.Models.Dba;

namespace Runix.Desktop.Views.Dba;

public partial class RoleEditorDialog : UserControl
{
    private PostgresRoleInfo? _existingRole;

    public event Action<PostgresRoleInfo, string?>? SaveRequested;
    public event Action? CancelRequested;

    public RoleEditorDialog()
    {
        InitializeComponent();

        BtnCloseTop.Click += (s, e) => CancelRequested?.Invoke();
        BtnCancel.Click += (s, e) => CancelRequested?.Invoke();

        BtnTogglePassword.Click += (s, e) =>
        {
            if (TxtPassword.PasswordChar == '●')
            {
                TxtPassword.PasswordChar = '\0';
                BtnTogglePassword.Content = "🔒";
            }
            else
            {
                TxtPassword.PasswordChar = '●';
                BtnTogglePassword.Content = "👁";
            }
        };

        ChkNeverExpires.IsCheckedChanged += (s, e) =>
        {
            PanelDatePicker.IsVisible = ChkNeverExpires.IsChecked != true;
        };

        BtnSave.Click += OnSaveClicked;
    }

    public void SetupForNewRole()
    {
        _existingRole = null;
        TxtDialogTitle.Text = "Criar Nova Role / Usuário";
        TxtRoleName.Text = string.Empty;
        TxtRoleName.IsEnabled = true;
        TxtPassword.Text = string.Empty;
        ChkCanLogin.IsChecked = true;
        ChkSuperuser.IsChecked = false;
        ChkCreateDb.IsChecked = false;
        ChkCreateRole.IsChecked = false;
        ChkInherit.IsChecked = true;
        ChkReplication.IsChecked = false;
        ChkNeverExpires.IsChecked = true;
        PanelDatePicker.IsVisible = false;
        TxtError.IsVisible = false;
    }

    public void SetupForEditRole(PostgresRoleInfo role)
    {
        _existingRole = role;
        TxtDialogTitle.Text = $"Editar Role: {role.RoleName}";
        TxtRoleName.Text = role.RoleName;
        TxtRoleName.IsEnabled = false; // Name cannot be altered via simple ALTER ROLE without RENAME TO
        TxtPassword.Text = string.Empty;
        ChkCanLogin.IsChecked = role.CanLogin;
        ChkSuperuser.IsChecked = role.IsSuperuser;
        ChkCreateDb.IsChecked = role.CanCreateDb;
        ChkCreateRole.IsChecked = role.CanCreateRole;
        ChkInherit.IsChecked = role.InheritsPrivileges;
        ChkReplication.IsChecked = role.IsReplication;

        if (role.ValidUntil.HasValue)
        {
            ChkNeverExpires.IsChecked = false;
            PanelDatePicker.IsVisible = true;
            DpValidUntil.SelectedDate = new DateTimeOffset(role.ValidUntil.Value);
        }
        else
        {
            ChkNeverExpires.IsChecked = true;
            PanelDatePicker.IsVisible = false;
        }

        TxtError.IsVisible = false;
    }

    private void OnSaveClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var roleName = TxtRoleName.Text?.Trim();
        if (string.IsNullOrWhiteSpace(roleName))
        {
            TxtError.Text = "O nome da role é obrigatório.";
            TxtError.IsVisible = true;
            return;
        }

        DateTime? validUntil = null;
        if (ChkNeverExpires.IsChecked != true && DpValidUntil.SelectedDate.HasValue)
        {
            validUntil = DpValidUntil.SelectedDate.Value.DateTime;
        }

        var role = new PostgresRoleInfo
        {
            RoleName = roleName,
            CanLogin = ChkCanLogin.IsChecked == true,
            IsSuperuser = ChkSuperuser.IsChecked == true,
            CanCreateDb = ChkCreateDb.IsChecked == true,
            CanCreateRole = ChkCreateRole.IsChecked == true,
            InheritsPrivileges = ChkInherit.IsChecked == true,
            IsReplication = ChkReplication.IsChecked == true,
            ValidUntil = validUntil
        };

        var password = string.IsNullOrWhiteSpace(TxtPassword.Text) ? null : TxtPassword.Text;

        SaveRequested?.Invoke(role, password);
    }
}
