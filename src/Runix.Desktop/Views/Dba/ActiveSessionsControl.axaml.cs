using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using Runix.Core.Interfaces;
using Runix.Core.Models.Dba;
using Runix.Infrastructure.Dba;

namespace Runix.Desktop.Views.Dba;

public partial class ActiveSessionsControl : UserControl
{
    private IDatabaseService? _databaseService;
    private readonly DispatcherTimer _timer = new();
    private readonly List<PostgresActiveSession> _allSessions = new();
    private PostgresActiveSession? _selectedSession;
    private bool _isActionTerminate;
    private bool _isRefreshing;

    public event Action<string>? StatusMessageChanged;

    public ActiveSessionsControl()
    {
        InitializeComponent();

        ComboPolling.ItemsSource = new List<string> { "Pausado", "3 segundos", "5 segundos", "10 segundos", "30 segundos" };
        ComboPolling.SelectedIndex = 2; // Default 5s

        ComboPolling.SelectionChanged += (s, e) => UpdateTimerInterval();

        _timer.Tick += async (s, e) =>
        {
            if (!_isRefreshing) await LoadSessionsAsync(isBackgroundPoll: true);
        };

        BtnRefresh.Click += async (s, e) => await LoadSessionsAsync(isBackgroundPoll: false);
        TxtFilter.TextChanged += (s, e) => FilterSessions();

        GridSessions.SelectionChanged += OnGridSelectionChanged;

        BtnCancelQuery.Click += OnCancelQueryClicked;
        BtnTerminateBackend.Click += OnTerminateBackendClicked;

        BtnCancelAction.Click += (s, e) => PanelConfirmOverlay.IsVisible = false;
        BtnExecuteAction.Click += async (s, e) => await ExecuteConfirmedActionAsync();

        DetachedFromVisualTree += (s, e) => _timer.Stop();
    }

    public void Initialize(IDatabaseService databaseService)
    {
        _databaseService = databaseService;
        UpdateTimerInterval();
        _ = LoadSessionsAsync(isBackgroundPoll: false);
    }

    private void UpdateTimerInterval()
    {
        _timer.Stop();
        var selected = ComboPolling.SelectedItem?.ToString();
        var seconds = selected switch
        {
            "3 segundos" => 3,
            "5 segundos" => 5,
            "10 segundos" => 10,
            "30 segundos" => 30,
            _ => 0
        };

        if (seconds > 0)
        {
            _timer.Interval = TimeSpan.FromSeconds(seconds);
            _timer.Start();
        }
    }

    public async Task LoadSessionsAsync(bool isBackgroundPoll = false)
    {
        if (_databaseService == null || _isRefreshing) return;

        _isRefreshing = true;
        if (!isBackgroundPoll) PanelLoading.IsVisible = true;

        try
        {
            var sessions = await PostgresAdminService.Instance.GetActiveSessionsAsync(_databaseService);
            _allSessions.Clear();
            _allSessions.AddRange(sessions);

            FilterSessions();

            var activeCount = _allSessions.Count(s => s.IsActive);
            TxtSessionStats.Text = $"{_allSessions.Count} sessões ({activeCount} ativas)";
        }
        catch (Exception ex)
        {
            if (!isBackgroundPoll)
            {
                StatusMessageChanged?.Invoke($"Erro ao consultar sessões ativas: {ex.Message}");
            }
        }
        finally
        {
            _isRefreshing = false;
            PanelLoading.IsVisible = false;
        }
    }

    private void FilterSessions()
    {
        var filter = TxtFilter.Text?.Trim().ToLowerInvariant() ?? string.Empty;
        var filtered = _allSessions.AsEnumerable();

        if (!string.IsNullOrEmpty(filter))
        {
            filtered = filtered.Where(s =>
                s.UserName.ToLowerInvariant().Contains(filter) ||
                s.DatabaseName.ToLowerInvariant().Contains(filter) ||
                s.ApplicationName.ToLowerInvariant().Contains(filter) ||
                s.ClientAddress.ToLowerInvariant().Contains(filter) ||
                s.QueryText.ToLowerInvariant().Contains(filter) ||
                s.Pid.ToString().Contains(filter));
        }

        var list = filtered.ToList();
        GridSessions.ItemsSource = null;
        GridSessions.ItemsSource = list;

        // Re-evaluate selected item
        if (_selectedSession != null)
        {
            var match = list.FirstOrDefault(s => s.Pid == _selectedSession.Pid);
            UpdateSelection(match);
        }
        else
        {
            UpdateSelection(null);
        }
    }

    private void OnGridSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var selected = GridSessions.SelectedItem as PostgresActiveSession;
        UpdateSelection(selected);
    }

    private void UpdateSelection(PostgresActiveSession? session)
    {
        _selectedSession = session;
        if (session == null)
        {
            TxtSelectedSessionInfo.Text = "Selecione um processo para gerenciar";
            BtnCancelQuery.IsEnabled = false;
            BtnTerminateBackend.IsEnabled = false;
            return;
        }

        var currentTag = session.IsCurrentSession ? " [Sessão Atual RUNIX]" : "";
        TxtSelectedSessionInfo.Text = $"PID {session.Pid} ({session.UserName}@{session.DatabaseName}) - {session.State}{currentTag}";

        if (session.IsCurrentSession)
        {
            BtnCancelQuery.IsEnabled = false;
            BtnTerminateBackend.IsEnabled = false;
        }
        else
        {
            BtnCancelQuery.IsEnabled = session.IsActive;
            BtnTerminateBackend.IsEnabled = true;
        }
    }

    private void OnCancelQueryClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_selectedSession == null) return;

        _isActionTerminate = false;
        TxtConfirmIcon.Text = "✋";
        TxtConfirmTitle.Text = $"Cancelar Query (PID {_selectedSession.Pid})";
        TxtConfirmMessage.Text = $"Tem certeza que deseja cancelar a query em execução do usuário '{_selectedSession.UserName}' no banco '{_selectedSession.DatabaseName}'? A conexão do cliente continuará aberta.";
        TxtConfirmQuery.Text = string.IsNullOrWhiteSpace(_selectedSession.QueryText) ? "<nenhuma query>" : _selectedSession.QueryText;
        BtnExecuteAction.Content = "Cancelar Query";
        PanelConfirmOverlay.IsVisible = true;
    }

    private void OnTerminateBackendClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_selectedSession == null) return;

        _isActionTerminate = true;
        TxtConfirmIcon.Text = "☠";
        TxtConfirmTitle.Text = $"Encerrar Conexão (PID {_selectedSession.Pid})";
        TxtConfirmMessage.Text = $"ATENÇÃO: Isso forçará o encerramento do processo e fechará o socket TCP do cliente '{_selectedSession.UserName}' ({_selectedSession.ApplicationName}).";
        TxtConfirmQuery.Text = string.IsNullOrWhiteSpace(_selectedSession.QueryText) ? "<nenhuma query>" : _selectedSession.QueryText;
        BtnExecuteAction.Content = "Encerrar Conexão";
        PanelConfirmOverlay.IsVisible = true;
    }

    private async Task ExecuteConfirmedActionAsync()
    {
        if (_databaseService == null || _selectedSession == null) return;

        PanelConfirmOverlay.IsVisible = false;
        var pid = _selectedSession.Pid;

        try
        {
            if (_isActionTerminate)
            {
                var success = await PostgresAdminService.Instance.TerminateBackendAsync(_databaseService, pid);
                StatusMessageChanged?.Invoke(success
                    ? $"✓ Processo PID {pid} encerrado com sucesso."
                    : $"Falha ao encerrar processo PID {pid}.");
            }
            else
            {
                var success = await PostgresAdminService.Instance.CancelQueryAsync(_databaseService, pid);
                StatusMessageChanged?.Invoke(success
                    ? $"✓ Query do PID {pid} cancelada com sucesso."
                    : $"Falha ao cancelar query do PID {pid}.");
            }

            await LoadSessionsAsync(isBackgroundPoll: false);
        }
        catch (Exception ex)
        {
            StatusMessageChanged?.Invoke($"Erro ao executar ação: {ex.Message}");
        }
    }

    public void EmulateSessionsForTesting(IReadOnlyList<PostgresActiveSession> sessions)
    {
        _allSessions.Clear();
        _allSessions.AddRange(sessions);
        FilterSessions();
        var activeCount = _allSessions.Count(s => s.IsActive);
        TxtSessionStats.Text = $"{_allSessions.Count} sessões ({activeCount} ativas)";
    }
}
