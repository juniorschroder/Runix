using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
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

    public string CurrentDatabaseId { get; set; } = "current_diagram";
    public double ZoomScale => _scaleTransform.ScaleX;
    public event Action<double>? ZoomChanged;
    public event Action<ErdTableNode>? NodeDoubleClicked;
    public event Action<ErdTableNode>? EditTableRequested;
    public event Action<ErdTableNode, ColumnInfo, ErdTableNode, ColumnInfo?>? ConnectionRequested;
    public event Action<SubjectAreaModel>? SubjectAreaRemoved;

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

    /// <summary>
    /// Anima suavemente a transição das coordenadas das tabelas e Subject Areas (~250ms com interpolação ease-in-out).
    /// </summary>
    public async Task AnimateLayoutTransitionAsync(GraphLayoutOptions? options = null, int durationMs = 250)
    {
        if (_graph.Nodes.Count == 0) return;

        // Capturar posições iniciais
        var startNodes = _graph.Nodes.ToDictionary(n => n.TableName, n => (n.X, n.Y), StringComparer.OrdinalIgnoreCase);
        var startAreas = _graph.SubjectAreas.ToDictionary(a => a.Id, a => (a.X, a.Y, a.Width, a.Height));

        // Calcular layout alvo via Sugiyama
        var engine = new SugiyamaHierarchicalLayoutEngine();
        engine.ArrangeLayout(_graph, options);

        var targetNodes = _graph.Nodes.ToDictionary(n => n.TableName, n => (n.X, n.Y), StringComparer.OrdinalIgnoreCase);
        var targetAreas = _graph.SubjectAreas.ToDictionary(a => a.Id, a => (a.X, a.Y, a.Width, a.Height));

        // Restaurar posições de partida para animar suavemente
        foreach (var n in _graph.Nodes)
        {
            var (sx, sy) = startNodes[n.TableName];
            n.X = sx;
            n.Y = sy;
        }
        foreach (var a in _graph.SubjectAreas)
        {
            var (sx, sy, sw, sh) = startAreas[a.Id];
            a.X = sx;
            a.Y = sy;
            a.Width = sw;
            a.Height = sh;
        }
        _surface.UpdateNodePositions();

        var tcs = new TaskCompletionSource<bool>();
        var startTime = DateTime.UtcNow;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };

        timer.Tick += (s, e) =>
        {
            var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
            var progress = Math.Clamp(elapsed / durationMs, 0.0, 1.0);

            // Ease-in-out quadratic
            double ease = progress < 0.5
                ? 2.0 * progress * progress
                : 1.0 - (Math.Pow((-2.0 * progress) + 2.0, 2) / 2.0);

            foreach (var n in _graph.Nodes)
            {
                var (sx, sy) = startNodes[n.TableName];
                var (tx, ty) = targetNodes[n.TableName];
                n.X = sx + ((tx - sx) * ease);
                n.Y = sy + ((ty - sy) * ease);
            }

            foreach (var a in _graph.SubjectAreas)
            {
                var (sx, sy, sw, sh) = startAreas[a.Id];
                var (tx, ty, tw, th) = targetAreas[a.Id];
                a.X = sx + ((tx - sx) * ease);
                a.Y = sy + ((ty - sy) * ease);
                a.Width = sw + ((tw - sw) * ease);
                a.Height = sh + ((th - sh) * ease);
            }

            _surface.UpdateNodePositions();

            if (progress >= 1.0)
            {
                timer.Stop();
                foreach (var n in _graph.Nodes)
                {
                    var (tx, ty) = targetNodes[n.TableName];
                    n.X = tx;
                    n.Y = ty;
                }
                foreach (var a in _graph.SubjectAreas)
                {
                    var (tx, ty, tw, th) = targetAreas[a.Id];
                    a.X = tx;
                    a.Y = ty;
                    a.Width = tw;
                    a.Height = th;
                }
                _surface.UpdateNodePositions();
                CenterOnContent();
                ErdLayoutService.SaveLayout(CurrentDatabaseId, _graph);
                tcs.TrySetResult(true);
            }
        };

        timer.Start();
        await tcs.Task;
    }

    /// <summary>
    /// Centraliza suavemente o viewport do canvas na tabela especificada.
    /// </summary>
    public void PanToNode(string tableName)
    {
        var node = _graph.FindNode(tableName);
        if (node == null) return;

        double nodeW = node.Width > 0 ? node.Width : 250;
        double nodeH = node.Height > 0 ? node.Height : 200;
        double nodeCenterX = node.X + (nodeW / 2.0);
        double nodeCenterY = node.Y + (nodeH / 2.0);

        double viewWidth = Bounds.Width > 100 ? Bounds.Width : 1000;
        double viewHeight = Bounds.Height > 100 ? Bounds.Height : 700;

        _translateTransform.X = (viewWidth / 2.0) - (nodeCenterX * ZoomScale);
        _translateTransform.Y = (viewHeight / 2.0) - (nodeCenterY * ZoomScale);

        ClampTranslation();
    }

    /// <summary>
    /// Aplica destaque pulsante luminoso ciano (#00e5ff) no cartão da tabela encontrada.
    /// </summary>
    public void HighlightNodePulse(string tableName)
    {
        var card = _surface.GetNodeCard(tableName);
        if (card == null) return;

        card.ZIndex = 90;
        var origBrush = card.BorderBrush;
        var origThickness = card.BorderThickness;
        var glowBrush = Brush.Parse("#00e5ff");

        int ticks = 0;
        const int maxTicks = 8;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };

        timer.Tick += (s, e) =>
        {
            ticks++;
            bool isGlow = (ticks % 2) != 0;
            card.BorderBrush = isGlow ? glowBrush : origBrush;
            card.BorderThickness = isGlow ? new Thickness(3.0) : origThickness;

            if (ticks >= maxTicks)
            {
                timer.Stop();
                card.BorderBrush = origBrush;
                card.BorderThickness = origThickness;
                card.ZIndex = 0;
            }
        };

        timer.Start();
    }

    public void AddSubjectArea(SubjectAreaModel area)
    {
        _graph.SubjectAreas.Add(area);
        _surface.AddSubjectArea(area);
        ErdLayoutService.SaveLayout(CurrentDatabaseId, _graph);
    }

    public void RemoveSubjectArea(SubjectAreaModel area)
    {
        _graph.SubjectAreas.Remove(area);
        _surface.RemoveSubjectArea(area.Id);
        ErdLayoutService.SaveLayout(CurrentDatabaseId, _graph);
        SubjectAreaRemoved?.Invoke(area);
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
        private readonly Dictionary<string, Border> _areaCards = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConnectorOverlayControl _connectorLayer;

        private ErdTableNode? _draggedNode;
        private Border? _draggedCard;
        private Point _dragStartPos;
        private Point _nodeOrigPos;

        // Subject Area Batch Dragging State
        private SubjectAreaModel? _draggedArea;
        private Border? _draggedAreaCard;
        private Point _areaDragStartPos;
        private Point _areaOrigPos;
        private readonly Dictionary<string, Point> _containedNodesOrigPositions = new(StringComparer.OrdinalIgnoreCase);

        // Subject Area Resizing State
        private SubjectAreaModel? _resizingArea;
        private Border? _resizingAreaCard;
        private Point _resizeStartPos;
        private Size _origAreaSize;
        private TextBlock? _resizingCountText;

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
            _areaCards.Clear();

            // 1. Camada de Subject Areas (Fundo)
            foreach (var area in graph.SubjectAreas)
            {
                var areaCard = CreateSubjectAreaCard(area);
                _areaCards[area.Id] = areaCard;
                Children.Add(areaCard);

                SetLeft(areaCard, area.X);
                SetTop(areaCard, area.Y);
            }

            // 2. Camada de Conectores (Meio)
            Children.Add(_connectorLayer);

            // 3. Camada de Cards de Tabelas (Frente)
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

        public void AddSubjectArea(SubjectAreaModel area)
        {
            var card = CreateSubjectAreaCard(area);
            _areaCards[area.Id] = card;

            int connectorIndex = Children.IndexOf(_connectorLayer);
            if (connectorIndex >= 0)
            {
                Children.Insert(connectorIndex, card);
            }
            else
            {
                Children.Add(card);
            }

            SetLeft(card, area.X);
            SetTop(card, area.Y);
        }

        public void RemoveSubjectArea(string areaId)
        {
            if (_areaCards.TryGetValue(areaId, out var card))
            {
                Children.Remove(card);
                _areaCards.Remove(areaId);
            }
        }

        public Border? GetNodeCard(string tableName) =>
            _nodeCards.TryGetValue(tableName, out var card) ? card : null;

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

            foreach (var area in _graph.SubjectAreas)
            {
                if (_areaCards.TryGetValue(area.Id, out var areaCard))
                {
                    SetLeft(areaCard, area.X);
                    SetTop(areaCard, area.Y);
                    areaCard.Width = area.Width;
                    areaCard.Height = area.Height;
                }
            }

            _connectorLayer.InvalidateVisual();
        }

        private Border CreateSubjectAreaCard(SubjectAreaModel area)
        {
            var baseColor = Color.Parse(area.ColorHex);
            var bgColor = Color.FromArgb(28, baseColor.R, baseColor.G, baseColor.B);
            var headerColor = Color.FromArgb(60, baseColor.R, baseColor.G, baseColor.B);

            var areaCard = new Border
            {
                Width = area.Width,
                Height = area.Height,
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(bgColor),
                BorderBrush = new SolidColorBrush(baseColor),
                BorderThickness = new Thickness(1.5),
                ClipToBounds = true
            };

            var grid = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,*")
            };

            // Header
            var header = new Border
            {
                Background = new SolidColorBrush(headerColor),
                Padding = new Thickness(12, 6),
                Cursor = new Cursor(StandardCursorType.SizeAll)
            };

            var headerGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto")
            };

            var icon = new TextBlock
            {
                Text = "📁 ",
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(icon, 0);

            var title = new TextBlock
            {
                Text = area.Title,
                FontWeight = FontWeight.Bold,
                FontSize = 12,
                Foreground = Brush.Parse("#f0f6fc"),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(title, 1);

            var countText = new TextBlock
            {
                Text = $"{area.TableCount} tabelas",
                FontSize = 10,
                Foreground = new SolidColorBrush(baseColor),
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 8, 0)
            };
            Grid.SetColumn(countText, 2);

            var btnUngroup = new Button
            {
                Content = "✕",
                Classes = { "btn-icon" },
                Padding = new Thickness(4, 2),
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center
            };
            ToolTip.SetTip(btnUngroup, "Desagrupar Área");
            Grid.SetColumn(btnUngroup, 3);

            headerGrid.Children.Add(icon);
            headerGrid.Children.Add(title);
            headerGrid.Children.Add(countText);
            headerGrid.Children.Add(btnUngroup);
            header.Child = headerGrid;
            Grid.SetRow(header, 0);

            // Body with Resize Handle
            var bodyGrid = new Grid();
            Grid.SetRow(bodyGrid, 1);

            var resizeThumb = new Border
            {
                Width = 16,
                Height = 16,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Background = Brushes.Transparent,
                Cursor = new Cursor(StandardCursorType.BottomRightCorner),
                Margin = new Thickness(0, 0, 4, 4),
                Child = new TextBlock
                {
                    Text = "◢",
                    FontSize = 10,
                    Foreground = new SolidColorBrush(baseColor),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            bodyGrid.Children.Add(resizeThumb);

            grid.Children.Add(header);
            grid.Children.Add(bodyGrid);
            areaCard.Child = grid;

            // Events
            btnUngroup.Click += (s, e) =>
            {
                _parent.RemoveSubjectArea(area);
            };

            header.PointerPressed += (s, e) =>
            {
                if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                {
                    _draggedArea = area;
                    _draggedAreaCard = areaCard;
                    _areaDragStartPos = e.GetPosition(this);
                    _areaOrigPos = new Point(area.X, area.Y);

                    _containedNodesOrigPositions.Clear();
                    area.TableNames.Clear();

                    foreach (var node in _graph.Nodes)
                    {
                        if (area.ContainsTableCenter(node))
                        {
                            area.AddTable(node.TableName);
                            _containedNodesOrigPositions[node.TableName] = new Point(node.X, node.Y);
                        }
                    }

                    countText.Text = $"{area.TableCount} tabelas";
                    e.Handled = true;
                }
            };

            resizeThumb.PointerPressed += (s, e) =>
            {
                if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                {
                    _resizingArea = area;
                    _resizingAreaCard = areaCard;
                    _resizeStartPos = e.GetPosition(this);
                    _origAreaSize = new Size(area.Width, area.Height);
                    _resizingCountText = countText;
                    e.Handled = true;
                }
            };

            return areaCard;
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

            if (_draggedArea != null && _draggedAreaCard != null)
            {
                var cur = e.GetPosition(this);
                var delta = cur - _areaDragStartPos;

                _draggedArea.X = Math.Max(20, _areaOrigPos.X + delta.X);
                _draggedArea.Y = Math.Max(20, _areaOrigPos.Y + delta.Y);

                SetLeft(_draggedAreaCard, _draggedArea.X);
                SetTop(_draggedAreaCard, _draggedArea.Y);

                foreach (var (tableName, origPos) in _containedNodesOrigPositions)
                {
                    var node = _graph.FindNode(tableName);
                    if (node != null)
                    {
                        node.X = Math.Max(20, origPos.X + delta.X);
                        node.Y = Math.Max(20, origPos.Y + delta.Y);

                        if (_nodeCards.TryGetValue(tableName, out var card))
                        {
                            SetLeft(card, node.X);
                            SetTop(card, node.Y);
                        }
                    }
                }

                _connectorLayer.InvalidateVisual();
                e.Handled = true;
                return;
            }

            if (_resizingArea != null && _resizingAreaCard != null)
            {
                var cur = e.GetPosition(this);
                var dw = cur.X - _resizeStartPos.X;
                var dh = cur.Y - _resizeStartPos.Y;

                _resizingArea.Width = Math.Max(240, _origAreaSize.Width + dw);
                _resizingArea.Height = Math.Max(160, _origAreaSize.Height + dh);

                _resizingAreaCard.Width = _resizingArea.Width;
                _resizingAreaCard.Height = _resizingArea.Height;

                _resizingArea.TableNames.Clear();
                foreach (var node in _graph.Nodes)
                {
                    if (_resizingArea.ContainsTableCenter(node))
                    {
                        _resizingArea.AddTable(node.TableName);
                    }
                }

                if (_resizingCountText != null)
                {
                    _resizingCountText.Text = $"{_resizingArea.TableCount} tabelas";
                }

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

            if (_draggedArea != null)
            {
                _draggedArea = null;
                _draggedAreaCard = null;
                _containedNodesOrigPositions.Clear();
                ErdLayoutService.SaveLayout(_parent.CurrentDatabaseId, _graph);
                _connectorLayer.InvalidateVisual();
                e.Handled = true;
                return;
            }

            if (_resizingArea != null)
            {
                _resizingArea = null;
                _resizingAreaCard = null;
                _resizingCountText = null;
                ErdLayoutService.SaveLayout(_parent.CurrentDatabaseId, _graph);
                e.Handled = true;
                return;
            }

            if (_draggedNode != null && _draggedCard != null)
            {
                _draggedCard.BorderBrush = Brush.Parse("#1e2d42");
                _draggedCard.ZIndex = 0;

                // Atualizar membership de subject areas
                foreach (var area in _graph.SubjectAreas)
                {
                    if (area.ContainsTableCenter(_draggedNode))
                    {
                        area.AddTable(_draggedNode.TableName);
                    }
                    else
                    {
                        area.RemoveTable(_draggedNode.TableName);
                    }
                }

                _draggedNode = null;
                _draggedCard = null;

                // Salva posições ajustadas
                ErdLayoutService.SaveLayout(_parent.CurrentDatabaseId, _graph);
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
