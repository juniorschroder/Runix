using System;
using Avalonia.Controls;
using Runix.Core.Models;

namespace Runix.Desktop.Views.Dialogs;

public partial class ErdExportDialog : UserControl
{
    private ErdGraphModel _graph = new();

    public event Action<ErdExportOptions>? ExportConfirmed;
    public event Action? CancelRequested;

    public ErdExportDialog()
    {
        InitializeComponent();

        BtnClose.Click += (s, e) => CancelRequested?.Invoke();
        BtnCancel.Click += (s, e) => CancelRequested?.Invoke();
        BtnConfirmExport.Click += (s, e) => OnConfirm();

        RadioPdf.IsCheckedChanged += (s, e) => UpdatePanelsVisibility();
        RadioSvg.IsCheckedChanged += (s, e) => UpdatePanelsVisibility();
        RadioPng.IsCheckedChanged += (s, e) => UpdatePanelsVisibility();
        RadioSql.IsCheckedChanged += (s, e) => UpdatePanelsVisibility();
    }

    public void Setup(ErdGraphModel graph)
    {
        _graph = graph ?? new ErdGraphModel();

        var box = DiagramBoundingBox.Calculate(_graph, 32.0);
        int nodeCount = _graph.Nodes.Count;
        int areaCount = _graph.SubjectAreas?.Count ?? 0;
        int relCount = _graph.Relationships?.Count ?? 0;

        TxtSummary.Text = $"{nodeCount} tabelas  •  {relCount} relacionamentos  •  {areaCount} áreas  •  Dimensão: {(int)box.Width} × {(int)box.Height} px";

        UpdatePanelsVisibility();
    }

    private void UpdatePanelsVisibility()
    {
        bool isPdf = RadioPdf.IsChecked == true;
        bool isSvg = RadioSvg.IsChecked == true;
        bool isPng = RadioPng.IsChecked == true;
        bool isSql = RadioSql.IsChecked == true;

        PanelPdfPageSize.IsVisible = isPdf;
        PanelTheme.IsVisible = isPdf || isSvg;
    }

    private void OnConfirm()
    {
        var options = new ErdExportOptions();

        if (RadioSvg.IsChecked == true)
        {
            options.Format = ErdExportFormat.Svg;
        }
        else if (RadioPng.IsChecked == true)
        {
            options.Format = ErdExportFormat.Png;
        }
        else if (RadioSql.IsChecked == true)
        {
            options.Format = ErdExportFormat.Sql;
        }
        else
        {
            options.Format = ErdExportFormat.Pdf;
        }

        options.Theme = RadioThemeLight.IsChecked == true
            ? ErdExportTheme.LightPrint
            : ErdExportTheme.DarkRunix;

        options.PageSize = CmbPdfPageSize.SelectedIndex switch
        {
            1 => ErdPdfPageSize.A4Landscape,
            2 => ErdPdfPageSize.A3Landscape,
            _ => ErdPdfPageSize.FitToDiagram
        };

        ExportConfirmed?.Invoke(options);
    }
}
