using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Runix.Core.Models;
using Runix.Core.Services;

namespace Runix.Desktop.Views;

/// <summary>
/// Canvas interativo em Avalonia UI para renderização de nós de tabela e conectores Bezier com pan, zoom, drag & drop e modelagem de FKs.
/// </summary>
public class ErdCanvasControl : Panel
{
    private ErdGraphModel _graph = new();
    private readonly DiagramSurfaceCanvas _surface;
    private readonly ScaleTransform _scaleTransform = new(1.0, 1.0);
    private readonly TranslateTransform _translateTransform = new(40.0, 40.0);
    private readonly TransformGroup _transformGroup = new();

    private bool _isPanning;
    private Point _panStart;
    private bool _hasInitiallyCentered;

    public double ZoomScale => _scaleTransform.ScaleX;
    public event Action<double>? ZoomChanged;
    public event Action<ErdTableNode>? NodeDoubleClicked;
    public event Action<ErdTableNode>? EditTableRequested;
    public event Action<ErdTableNode, ColumnInfo, ErdTableNode, ColumnInfo?>? ConnectionRequested;

    public ErdCanvasControl()
    {
        ClipToBounds = true;
        Background = Brush.Parse("#090d14");

        _transformGroup.Children.Add(_scaleTransform);
        _transformGroup.Children.Add(_translateTransform);

        _surface = new DiagramSurfaceCanvas(this)
        {
            RenderTransform = _transformGroup,
            RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Relative),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        };

        Children.Add(_surface);

        SizeChanged += (s, e) =>
        {
            if (!_hasInitiallyCentered && Bounds.Width > 100 && Bounds.Height > 100 && _graph.Nodes.Count > 0)
            {
                _hasInitiallyCentered = true;
                CenterOnContent();
            }
        };

        PointerWheelChanged += OnPointerWheelChanged;
        PointerPressed += OnPanelPointerPressed;
        PointerMoved += OnPanelPointerMoved;
        PointerReleased += OnPanelPointerReleased;
    }

    internal void OnNodeDoubleClicked(ErdTableNode node)
    {
        NodeDoubleClicked?.Invoke(node);
        EditTableRequested?.Invoke(node);
    }

    internal void OnConnectionRequested(ErdTableNode sourceNode, ColumnInfo sourceCol, ErdTableNode targetNode, ColumnInfo? targetCol)
    {
        ConnectionRequested?.Invoke(sourceNode, sourceCol, targetNode, targetCol);
    }

    public void SetGraph(ErdGraphModel graph)
    {
        _graph = graph;
        _surface.LoadGraph(graph);

        if (Bounds.Width > 100 && Bounds.Height > 100 && _graph.Nodes.Count > 0)
        {
            _hasInitiallyCentered = true;
            CenterOnContent();
        }
        else
        {
            _hasInitiallyCentered = false;
            _scaleTransform.ScaleX = 1.0;
            _scaleTransform.ScaleY = 1.0;
            _translateTransform.X = 40.0;
            _translateTransform.Y = 40.0;
            ZoomChanged?.Invoke(1.0);
        }
    }

    public ErdGraphModel GetGraph() => _graph;

    public void ResetView()
    {
        CenterOnContent();
    }

    public void CenterOnContent()
    {
        if (_graph.Nodes.Count == 0)
        {
            _scaleTransform.ScaleX = 1.0;
            _scaleTransform.ScaleY = 1.0;
            _translateTransform.X = 40;
            _translateTransform.Y = 40;
            ZoomChanged?.Invoke(1.0);
            return;
        }

        double minX = _graph.Nodes.Min(n => n.X);
        double minY = _graph.Nodes.Min(n => n.Y);
        double maxX = _graph.Nodes.Max(n => n.X + (n.Width > 0 ? n.Width : 250));
        double maxY = _graph.Nodes.Max(n => n.Y + (n.Height > 0 ? n.Height : 200));

        double contentWidth = Math.Max(100, maxX - minX);
        double contentHeight = Math.Max(100, maxY - minY);

        double viewWidth = Bounds.Width > 100 ? Bounds.Width : 1000;
        double viewHeight = Bounds.Height > 100 ? Bounds.Height : 700;

        double scaleX = (viewWidth - 100) / contentWidth;
        double scaleY = (viewHeight - 100) / contentHeight;
        double fitScale = Math.Clamp(Math.Min(scaleX, scaleY), 0.25, 1.0);

        _scaleTransform.ScaleX = fitScale;
        _scaleTransform.ScaleY = fitScale;

        double contentCenterX = (minX + maxX) / 2.0;
        double contentCenterY = (minY + maxY) / 2.0;

        _translateTransform.X = (viewWidth / 2.0) - (contentCenterX * fitScale);
        _translateTransform.Y = (viewHeight / 2.0) - (contentCenterY * fitScale);

        ClampTranslation();
        ZoomChanged?.Invoke(fitScale);
    }

    public void SetZoom(double zoom)
    {
        var center = new Point(Bounds.Width > 50 ? Bounds.Width / 2 : 500, Bounds.Height > 50 ? Bounds.Height / 2 : 350);
        ZoomAt(center, zoom);
    }

    public void ZoomAt(Point anchor, double targetZoom)
    {
        var clamped = Math.Clamp(targetZoom, 0.20, 2.5);
        var oldZoom = ZoomScale;
        if (Math.Abs(clamped - oldZoom) < 0.001) return;

        // Logical position in diagram before zoom (origin is (0,0))
        double diagramX = (anchor.X - _translateTransform.X) / oldZoom;
        double diagramY = (anchor.Y - _translateTransform.Y) / oldZoom;

        _scaleTransform.ScaleX = clamped;
        _scaleTransform.ScaleY = clamped;

        // Adjust translation so diagram point remains under the anchor
        _translateTransform.X = anchor.X - (diagramX * clamped);
        _translateTransform.Y = anchor.Y - (diagramY * clamped);

        ClampTranslation();
        ZoomChanged?.Invoke(clamped);
    }

    public void Pan(double dx, double dy)
    {
        _translateTransform.X += dx;
        _translateTransform.Y += dy;
        ClampTranslation();
    }

    private void ClampTranslation()
    {
        if (_graph.Nodes.Count == 0) return;

        double viewW = Bounds.Width > 50 ? Bounds.Width : 1000;
        double viewH = Bounds.Height > 50 ? Bounds.Height : 700;
        double zoom = ZoomScale;

        double minX = _graph.Nodes.Min(n => n.X) * zoom;
        double minY = _graph.Nodes.Min(n => n.Y) * zoom;
        double maxX = _graph.Nodes.Max(n => n.X + (n.Width > 0 ? n.Width : 250)) * zoom;
        double maxY = _graph.Nodes.Max(n => n.Y + (n.Height > 0 ? n.Height : 200)) * zoom;

        // Ensure at least 100px of diagram content remains visible in viewport
        double minTranslateX = 100 - maxX;
        double maxTranslateX = viewW - 100 - minX;

        double minTranslateY = 100 - maxY;
        double maxTranslateY = viewH - 100 - minY;

        if (minTranslateX <= maxTranslateX)
            _translateTransform.X = Math.Clamp(_translateTransform.X, minTranslateX, maxTranslateX);

        if (minTranslateY <= maxTranslateY)
            _translateTransform.Y = Math.Clamp(_translateTransform.Y, minTranslateY, maxTranslateY);
    }

    public void ZoomIn()
    {
        var center = new Point(Bounds.Width > 50 ? Bounds.Width / 2 : 500, Bounds.Height > 50 ? Bounds.Height / 2 : 350);
        ZoomAt(center, ZoomScale + 0.15);
    }

    public void ZoomOut()
    {
        var center = new Point(Bounds.Width > 50 ? Bounds.Width / 2 : 500, Bounds.Height > 50 ? Bounds.Height / 2 : 350);
        ZoomAt(center, ZoomScale - 0.15);
    }

    public void RefreshLayout()
    {
        _surface.UpdateNodePositions();
        _surface.InvalidateVisual();
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        var pos = e.GetPosition(this);
        bool isZoom = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);

        if (isZoom)
        {
            // Smooth zoom anchored at cursor (macOS pinch-to-zoom or Ctrl+wheel)
            double zoomFactor = 1.0 + Math.Clamp(e.Delta.Y * 0.015, -0.06, 0.06);
            ZoomAt(pos, ZoomScale * zoomFactor);
        }
        else
        {
            // Two-finger trackpad panning or mouse wheel scrolling
            double panFactor = 8.0;
            double dx = e.Delta.X * panFactor;
            double dy = e.Delta.Y * panFactor;

            Pan(dx, dy);
        }
        e.Handled = true;
    }

    private void OnPanelPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var prop = e.GetCurrentPoint(this).Properties;
        if (prop.IsMiddleButtonPressed || (prop.IsLeftButtonPressed && e.Source == this))
        {
            _isPanning = true;
            _panStart = e.GetPosition(this);
            Cursor = new Cursor(StandardCursorType.Hand);
            e.Handled = true;
        }
    }

    private void OnPanelPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_isPanning)
        {
            var cur = e.GetPosition(this);
            var delta = cur - _panStart;
            _panStart = cur;

            Pan(delta.X, delta.Y);
            e.Handled = true;
        }
    }

    private void OnPanelPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isPanning)
        {
            _isPanning = false;
            Cursor = Cursor.Default;
            e.Handled = true;
        }
    }

    /// <summary>
    /// Superfície interna de renderização do grafo relacional com coordenadas lógicas.
    /// </summary>
    private class DiagramSurfaceCanvas : Canvas
    {
        private readonly ErdCanvasControl _parent;
        private ErdGraphModel _graph = new();
        private readonly Dictionary<string, Border> _nodeCards = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConnectorOverlayControl _connectorLayer;

        private ErdTableNode? _draggedNode;
        private Border? _draggedCard;
        private Point _dragStartPos;
        private Point _nodeOrigPos;

        // Interactive Connection State
        private bool _isConnecting;
        private ErdTableNode? _connectingSourceNode;
        private ColumnInfo? _connectingSourceCol;
        private Point _connectingStartPos;
        private Point _connectingCurrentPos;

        public DiagramSurfaceCanvas(ErdCanvasControl parent)
        {
            _parent = parent;
            Width = 10000;
            Height = 10000;
            Background = Brushes.Transparent;

            _connectorLayer = new ConnectorOverlayControl(this);
            Children.Add(_connectorLayer);
        }

        public void LoadGraph(ErdGraphModel graph)
        {
            _graph = graph;
            Children.Clear();
            _nodeCards.Clear();

            // Adiciona a camada de conectores primeiro (fundo)
            Children.Add(_connectorLayer);

            foreach (var node in graph.Nodes)
            {
                var card = CreateTableCard(node);
                _nodeCards[node.TableName] = card;
                Children.Add(card);

                SetLeft(card, node.X);
                SetTop(card, node.Y);
            }

            _connectorLayer.InvalidateVisual();
        }

        public void UpdateNodePositions()
        {
            foreach (var node in _graph.Nodes)
            {
                if (_nodeCards.TryGetValue(node.TableName, out var card))
                {
                    SetLeft(card, node.X);
                    SetTop(card, node.Y);
                }
            }
            _connectorLayer.InvalidateVisual();
        }

        private Border CreateTableCard(ErdTableNode node)
        {
            var card = new Border
            {
                Width = node.Width,
                CornerRadius = new CornerRadius(8),
                Background = Brush.Parse("#141d2b"),
                BorderBrush = Brush.Parse("#1e2d42"),
                BorderThickness = new Thickness(1.5),
                ClipToBounds = true
            };

            var mainStack = new StackPanel();

            // HEADER
            var header = new Border
            {
                Background = Brush.Parse("#0e141f"),
                Padding = new Thickness(12, 8),
                BorderBrush = Brush.Parse("#1e2d42"),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Cursor = new Cursor(StandardCursorType.SizeAll)
            };

            var headerGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto")
            };

            var iconText = new TextBlock
            {
                Text = "🗄",
                FontSize = 13,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(iconText, 0);

            var titleText = new TextBlock
            {
                Text = node.TableName,
                FontWeight = FontWeight.Bold,
                FontSize = 13,
                Foreground = Brush.Parse("#f0f6fc"),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(titleText, 1);

            var countBadge = new Border
            {
                Background = Brush.Parse("#1a3a5c"),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2),
                Child = new TextBlock
                {
                    Text = node.Columns.Count.ToString(),
                    FontSize = 10,
                    Foreground = Brush.Parse("#38bdf8"),
                    FontWeight = FontWeight.Bold
                }
            };
            Grid.SetColumn(countBadge, 2);

            headerGrid.Children.Add(iconText);
            headerGrid.Children.Add(titleText);
            headerGrid.Children.Add(countBadge);
            header.Child = headerGrid;

            // DRAG EVENTS ON HEADER
            header.PointerPressed += (s, e) =>
            {
                if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                {
                    _draggedNode = node;
                    _draggedCard = card;
                    _dragStartPos = e.GetPosition(this);
                    _nodeOrigPos = new Point(node.X, node.Y);
                    card.BorderBrush = Brush.Parse("#008bb9");
                    card.ZIndex = 50;
                    e.Handled = true;
                }
            };

            header.DoubleTapped += (s, e) =>
            {
                _parent.OnNodeDoubleClicked(node);
                e.Handled = true;
            };

            mainStack.Children.Add(header);

            // COLUMNS LIST
            var colPanel = new StackPanel { Spacing = 1, Margin = new Thickness(0, 4) };
            for (int colIdx = 0; colIdx < node.Columns.Count; colIdx++)
            {
                var col = node.Columns[colIdx];
                var capturedCol = col;
                var capturedIdx = colIdx;

                var isFk = _graph.Relationships.Any(r =>
                    string.Equals(r.SourceTable, node.TableName, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(r.SourceColumn, col.Name, StringComparison.OrdinalIgnoreCase));

                var colRow = new Border
                {
                    Padding = new Thickness(10, 4),
                    Background = Brushes.Transparent
                };

                var colGrid = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto")
                };

                var colIcon = new TextBlock
                {
                    Text = col.IsPrimaryKey ? "🔑" : (isFk ? "🔗" : "•"),
                    FontSize = 11,
                    Foreground = col.IsPrimaryKey ? Brush.Parse("#eab308") : (isFk ? Brush.Parse("#008bb9") : Brush.Parse("#5a6f87")),
                    Margin = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(colIcon, 0);

                var colName = new TextBlock
                {
                    Text = col.Name,
                    FontSize = 12,
                    Foreground = col.IsPrimaryKey ? Brush.Parse("#f0f6fc") : Brush.Parse("#d1d5db"),
                    FontWeight = col.IsPrimaryKey ? FontWeight.Bold : FontWeight.Normal,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                Grid.SetColumn(colName, 1);

                var colType = new TextBlock
                {
                    Text = col.DataType,
                    FontSize = 10,
                    Foreground = Brush.Parse("#8b9cb3"),
                    FontFamily = new FontFamily("Consolas,Menlo,Monaco,monospace"),
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(colType, 2);

                // INTERACTIVE FK ANCHOR HANDLE
                var anchorHandle = new Border
                {
                    Width = 16,
                    Height = 16,
                    CornerRadius = new CornerRadius(8),
                    Background = Brush.Parse("#1a3a5c"),
                    BorderBrush = Brush.Parse("#38bdf8"),
                    BorderThickness = new Thickness(1),
                    Margin = new Thickness(6, 0, 0, 0),
                    Cursor = new Cursor(StandardCursorType.Cross),
                    Child = new TextBlock
                    {
                        Text = "○",
                        FontSize = 10,
                        Foreground = Brush.Parse("#38bdf8"),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                };
                ToolTip.SetTip(anchorHandle, "Arrastar para criar relacionamento (FK)");
                Grid.SetColumn(anchorHandle, 3);

                anchorHandle.PointerPressed += (s, e) =>
                {
                    if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                    {
                        _isConnecting = true;
                        _connectingSourceNode = node;
                        _connectingSourceCol = capturedCol;
                        _connectingStartPos = new Point(node.X + node.Width, node.Y + 40 + (capturedIdx * 25) + 12);
                        _connectingCurrentPos = e.GetPosition(this);
                        _connectorLayer.InvalidateVisual();
                        e.Handled = true;
                    }
                };

                colGrid.Children.Add(colIcon);
                colGrid.Children.Add(colName);
                colGrid.Children.Add(colType);
                colGrid.Children.Add(anchorHandle);
                colRow.Child = colGrid;

                colPanel.Children.Add(colRow);
            }

            mainStack.Children.Add(colPanel);
            card.Child = mainStack;
            return card;
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);

            if (_isConnecting)
            {
                _connectingCurrentPos = e.GetPosition(this);
                _connectorLayer.InvalidateVisual();
                e.Handled = true;
                return;
            }

            if (_draggedNode != null && _draggedCard != null)
            {
                var cur = e.GetPosition(this);
                var delta = cur - _dragStartPos;

                _draggedNode.X = Math.Max(20, _nodeOrigPos.X + delta.X);
                _draggedNode.Y = Math.Max(20, _nodeOrigPos.Y + delta.Y);

                SetLeft(_draggedCard, _draggedNode.X);
                SetTop(_draggedCard, _draggedNode.Y);

                _connectorLayer.InvalidateVisual();
                e.Handled = true;
            }
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);

            if (_isConnecting)
            {
                var dropPos = e.GetPosition(this);
                ErdTableNode? targetNode = null;
                ColumnInfo? targetCol = null;

                foreach (var (tableName, _) in _nodeCards)
                {
                    var candidate = _graph.FindNode(tableName);
                    if (candidate == null || candidate == _connectingSourceNode) continue;

                    var rect = new Rect(candidate.X, candidate.Y, candidate.Width, candidate.Height);
                    if (rect.Contains(dropPos))
                    {
                        targetNode = candidate;
                        var relY = dropPos.Y - (candidate.Y + 40);
                        var colIdx = (int)(relY / 25);
                        if (colIdx >= 0 && colIdx < candidate.Columns.Count)
                        {
                            targetCol = candidate.Columns[colIdx];
                        }
                        break;
                    }
                }

                if (targetNode != null && _connectingSourceNode != null && _connectingSourceCol != null)
                {
                    _parent.OnConnectionRequested(_connectingSourceNode, _connectingSourceCol, targetNode, targetCol);
                }

                _isConnecting = false;
                _connectingSourceNode = null;
                _connectingSourceCol = null;
                _connectorLayer.InvalidateVisual();
                e.Handled = true;
                return;
            }

            if (_draggedNode != null && _draggedCard != null)
            {
                _draggedCard.BorderBrush = Brush.Parse("#1e2d42");
                _draggedCard.ZIndex = 0;
                _draggedNode = null;
                _draggedCard = null;

                // Salva posições ajustadas
                ErdLayoutService.SaveLayout("current_diagram", _graph);
                _connectorLayer.InvalidateVisual();
                e.Handled = true;
            }
        }

        public void RenderConnectors(DrawingContext context)
        {
            // 1. GRID BACKGROUND PATTERN
            var gridPen = new Pen(Brush.Parse("#101726"), 1.0);
            for (double x = 0; x < 10000; x += 40)
            {
                context.DrawLine(gridPen, new Point(x, 0), new Point(x, 10000));
            }
            for (double y = 0; y < 10000; y += 40)
            {
                context.DrawLine(gridPen, new Point(0, y), new Point(10000, y));
            }

            // 2. ACTIVE INTERACTIVE RUBBERBAND BEZIER CONNECTION
            if (_isConnecting && _connectingSourceNode != null)
            {
                var rubberPen = new Pen(Brush.Parse("#38bdf8"), 2.0, DashStyle.Dash);
                var startPt = _connectingStartPos;
                var endPt = _connectingCurrentPos;
                var dx = Math.Max(50, Math.Abs(endPt.X - startPt.X) * 0.5);

                var cp1 = new Point(startPt.X + dx, startPt.Y);
                var cp2 = new Point(endPt.X - dx, endPt.Y);

                var geomRubber = new StreamGeometry();
                using (var ctx = geomRubber.Open())
                {
                    ctx.BeginFigure(startPt, false);
                    ctx.CubicBezierTo(cp1, cp2, endPt);
                }
                context.DrawGeometry(null, rubberPen, geomRubber);
                context.DrawEllipse(Brush.Parse("#38bdf8"), null, endPt, 4, 4);
            }

            // 3. BEZIER CONNECTOR LINES & CARDINALITY BADGES
            if (_graph.Relationships == null || _graph.Relationships.Count == 0) return;

            var linePen = new Pen(Brush.Parse("#008bb9"), 2.0);
            var badgeBg = Brush.Parse("#0e141f");
            var badgeBorder = new Pen(Brush.Parse("#0064a5"), 1.0);

            foreach (var rel in _graph.Relationships)
            {
                var sourceNode = _graph.FindNode(rel.SourceTable);
                var targetNode = _graph.FindNode(rel.TargetTable);
                if (sourceNode == null || targetNode == null) continue;

                var srcColIdx = Math.Max(0, sourceNode.Columns.FindIndex(c => string.Equals(c.Name, rel.SourceColumn, StringComparison.OrdinalIgnoreCase)));
                var tgtColIdx = Math.Max(0, targetNode.Columns.FindIndex(c => string.Equals(c.Name, rel.TargetColumn, StringComparison.OrdinalIgnoreCase)));

                var srcY = sourceNode.Y + 40 + (srcColIdx * 25) + 12;
                var tgtY = targetNode.Y + 40 + (tgtColIdx * 25) + 12;

                Point srcPt;
                Point tgtPt;
                double dx;

                if (sourceNode.X + sourceNode.Width <= targetNode.X)
                {
                    srcPt = new Point(sourceNode.X + sourceNode.Width, srcY);
                    tgtPt = new Point(targetNode.X, tgtY);
                    dx = Math.Max(60, Math.Abs(tgtPt.X - srcPt.X) * 0.5);
                }
                else if (targetNode.X + targetNode.Width <= sourceNode.X)
                {
                    srcPt = new Point(sourceNode.X, srcY);
                    tgtPt = new Point(targetNode.X + targetNode.Width, tgtY);
                    dx = -Math.Max(60, Math.Abs(srcPt.X - tgtPt.X) * 0.5);
                }
                else
                {
                    srcPt = new Point(sourceNode.X + sourceNode.Width, srcY);
                    tgtPt = new Point(targetNode.X + targetNode.Width, tgtY);
                    dx = 80;
                }

                var cp1 = new Point(srcPt.X + dx, srcPt.Y);
                var cp2 = new Point(tgtPt.X - dx, tgtPt.Y);

                var geom = new StreamGeometry();
                using (var ctx = geom.Open())
                {
                    ctx.BeginFigure(srcPt, false);
                    ctx.CubicBezierTo(cp1, cp2, tgtPt);
                }
                context.DrawGeometry(null, linePen, geom);

                var srcLabel = rel.Cardinality == ErdCardinality.OneToOne ? "1" : (rel.Cardinality == ErdCardinality.ManyToMany ? "M" : "N");
                DrawCardinalityBadge(context, srcPt, srcLabel, badgeBg, badgeBorder);
                DrawCardinalityBadge(context, tgtPt, "1", badgeBg, badgeBorder);
            }
        }

        private static void DrawCardinalityBadge(DrawingContext context, Point pt, string text, IBrush bg, Pen border)
        {
            var badgeRect = new Rect(pt.X - 9, pt.Y - 9, 18, 18);
            context.DrawRectangle(bg, border, badgeRect, 4, 4);

            var ft = new FormattedText(
                text,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface("Inter", FontStyle.Normal, FontWeight.Bold),
                10,
                Brush.Parse("#38bdf8"));

            var textPt = new Point(badgeRect.Center.X - (ft.Width / 2), badgeRect.Center.Y - (ft.Height / 2));
            context.DrawText(ft, textPt);
        }

        private class ConnectorOverlayControl : Control
        {
            private readonly DiagramSurfaceCanvas _surface;

            public ConnectorOverlayControl(DiagramSurfaceCanvas surface)
            {
                _surface = surface;
                Width = 10000;
                Height = 10000;
                ZIndex = -1;
            }

            public override void Render(DrawingContext context)
            {
                _surface.RenderConnectors(context);
            }
        }
    }
}
