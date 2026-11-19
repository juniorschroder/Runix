using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Runix.Core.Interfaces;
using Runix.Core.Models;
using Runix.Infrastructure.Data;
using Runix.Infrastructure.Database;

namespace Runix.Desktop.Views;

public partial class GlobalSearchTabControl : UserControl, IDisposable
{
    private readonly IDatabaseService _databaseService;
    private readonly IGlobalDataSearchService _searchService;
    private readonly ObservableCollection<GlobalSearchTableMatch> _matches = new();
    private CancellationTokenSource? _cts;
    private GlobalSearchTableMatch? _selectedMatch;

    public event Action<string, string>? OpenFilteredTableRequested;

    public GlobalSearchTabControl() : this(new SqliteDatabaseService(new ConnectionConfig()))
    {
    }

    public GlobalSearchTabControl(IDatabaseService databaseService, IGlobalDataSearchService? searchService = null)
    {
        InitializeComponent();
        _databaseService = databaseService;
        _searchService = searchService ?? new GlobalDataSearchService(databaseService);

        ListMatchedTables.ItemsSource = _matches;
        DrawerInspector.SetEditable(false);

        SetupEvents();
    }

    private void SetupEvents()
    {
        BtnRunSearch.Click += async (s, e) => await ExecuteGlobalSearchAsync();
        BtnCancelSearch.Click += (s, e) => _cts?.Cancel();

        TxtSearchTerm.KeyDown += async (s, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await ExecuteGlobalSearchAsync();
            }
        };

        ListMatchedTables.SelectionChanged += (s, e) =>
        {
            if (ListMatchedTables.SelectedItem is GlobalSearchTableMatch match)
            {
                DisplayMatchRows(match);
            }
        };

        BtnOpenInTableTab.Click += (s, e) =>
        {
            if (_selectedMatch != null)
            {
                OpenFilteredTableRequested?.Invoke(_selectedMatch.TableName, _selectedMatch.FilterSqlClause);
            }
        };

        BtnToggleInspector.Click += (s, e) =>
        {
            DrawerInspector.IsVisible = !DrawerInspector.IsVisible;
            BtnToggleInspector.Classes.Clear();
            BtnToggleInspector.Classes.Add(DrawerInspector.IsVisible ? "btn-secondary" : "btn-outline");
            if (DrawerInspector.IsVisible)
            {
                SyncInspectorWithSelectedRow();
            }
        };

        DrawerInspector.CloseRequested += () =>
        {
            DrawerInspector.IsVisible = false;
            BtnToggleInspector.Classes.Clear();
            BtnToggleInspector.Classes.Add("btn-outline");
        };

        GridSampleRows.SelectionChanged += (s, e) =>
        {
            if (DrawerInspector.IsVisible)
            {
                SyncInspectorWithSelectedRow();
            }
        };
    }

    public async Task ExecuteGlobalSearchAsync()
    {
        string term = (TxtSearchTerm.Text ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(term))
        {
            TxtStatus.Text = "Informe um termo de busca.";
            TxtStatus.Foreground = Brush.Parse("#fbbf24");
            return;
        }

        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        int maxRows = 25;
        if (CmbMaxRows.SelectedItem is ComboBoxItem cbi && int.TryParse(cbi.Content?.ToString(), out int parsed))
        {
            maxRows = parsed;
        }

        var options = new GlobalSearchOptions
        {
            SearchTerm = term,
            CaseSensitive = ChkCaseSensitive.IsChecked == true,
            MaxRowsPerTable = maxRows
        };

        BtnRunSearch.IsEnabled = false;
        BtnCancelSearch.IsVisible = true;
        ProgressScan.IsVisible = true;
        _matches.Clear();
        GridSampleRows.Columns.Clear();
        GridSampleRows.ItemsSource = null;
        BtnOpenInTableTab.IsEnabled = false;

        var progress = new Progress<string>(msg =>
        {
            TxtStatus.Text = msg;
            TxtStatus.Foreground = Brush.Parse("#38bdf8");
        });

        try
        {
            var report = await _searchService.SearchAcrossDatabaseAsync(options, progress, _cts.Token);
            foreach (var m in report.Matches)
            {
                _matches.Add(m);
            }

            TxtTablesHeader.Text = $"Tabelas com Ocorrências ({report.TablesWithMatches}/{report.TablesScanned})";
            TxtStatus.Text = $"✓ Encontrados {report.TotalMatchedRows:N0} registros em {report.TablesWithMatches} tabela(s) ({report.ElapsedMilliseconds:F1} ms)";
            TxtStatus.Foreground = report.TotalMatchedRows > 0 ? Brush.Parse("#34d399") : Brush.Parse("#8b9cb3");

            if (_matches.Count > 0)
            {
                ListMatchedTables.SelectedIndex = 0;
            }
            else
            {
                TxtActiveMatchTitle.Text = $"Nenhuma ocorrência encontrada para \"{term}\".";
            }
        }
        catch (OperationCanceledException)
        {
            TxtStatus.Text = "Busca global cancelada pelo usuário.";
            TxtStatus.Foreground = Brush.Parse("#fbbf24");
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"Erro na busca global: {ex.Message}";
            TxtStatus.Foreground = Brush.Parse("#f87171");
        }
        finally
        {
            BtnRunSearch.IsEnabled = true;
            BtnCancelSearch.IsVisible = false;
            ProgressScan.IsVisible = false;
        }
    }

    private void DisplayMatchRows(GlobalSearchTableMatch match)
    {
        _selectedMatch = match;
        BtnOpenInTableTab.IsEnabled = true;
        TxtActiveMatchTitle.Text = $"🗄 {match.DisplayTitle} — {match.MatchedRowCount} registro(s) • {match.MatchedColumnsSummary}";

        GridSampleRows.Columns.Clear();
        for (int i = 0; i < match.SampleResult.Columns.Count; i++)
        {
            int colIdx = i;
            string colName = match.SampleResult.Columns[i];
            GridSampleRows.Columns.Add(new DataGridTextColumn
            {
                Header = colName,
                Binding = new Binding($"[{colIdx}]")
            });
        }

        GridSampleRows.ItemsSource = match.SampleResult.Rows;
        if (match.SampleResult.Rows.Count > 0)
        {
            GridSampleRows.SelectedIndex = 0;
        }

        if (DrawerInspector.IsVisible)
        {
            SyncInspectorWithSelectedRow();
        }
    }

    private void SyncInspectorWithSelectedRow()
    {
        if (_selectedMatch != null && GridSampleRows.SelectedItem is IReadOnlyList<object?> row)
        {
            DrawerInspector.LoadRow(_selectedMatch.SampleResult.Columns, row);
        }
        else
        {
            DrawerInspector.LoadRow(Array.Empty<string>(), Array.Empty<object?>());
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        ListMatchedTables.ItemsSource = null;
        _matches.Clear();
        GridSampleRows.ItemsSource = null;
        GridSampleRows.Columns.Clear();
    }
}
