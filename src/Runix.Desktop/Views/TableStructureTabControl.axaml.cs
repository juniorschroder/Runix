using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Runix.Core.Interfaces;
using Runix.Core.Models;
using Runix.Desktop.Services;
using Runix.Infrastructure.Data;

namespace Runix.Desktop.Views;

public partial class TableStructureTabControl : UserControl
{
    private readonly IDatabaseService _databaseService;
    private readonly string _tableName;

    public string TableName => _tableName;

    public TableStructureTabControl() : this(new SqliteDatabaseService(new ConnectionConfig()), "")
    {
    }

    public TableStructureTabControl(IDatabaseService databaseService, string tableName)
    {
        InitializeComponent();
        _databaseService = databaseService;
        _tableName = tableName;
        TxtTableName.Text = tableName;

        SqlSyntaxHighlighting.ConfigureSqlEditor(TxtDdl);

        BtnRefresh.Click += (s, e) => LoadStructure();

        LoadStructure();
    }

    private async void LoadStructure()
    {
        try
        {
            var cols = await _databaseService.GetTableColumnsAsync(_tableName);
            GridColumns.ItemsSource = cols;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TableStructure] Error loading columns: {ex.Message}");
        }

        try
        {
            var idxs = await _databaseService.GetTableIndexesAsync(_tableName);
            GridIndexes.ItemsSource = idxs;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TableStructure] Error loading indexes: {ex.Message}");
        }

        try
        {
            var fks = await _databaseService.GetForeignKeysAsync(_tableName);
            GridForeignKeys.ItemsSource = fks;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TableStructure] Error loading foreign keys: {ex.Message}");
        }

        try
        {
            var ddl = await _databaseService.GetTableDdlAsync(_tableName);
            TxtDdl.Text = ddl;
        }
        catch (Exception ex)
        {
            TxtDdl.Text = $"-- Error loading DDL: {ex.Message}";
        }
    }
}
