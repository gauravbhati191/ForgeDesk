using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ForgeDesk.Domain;
using ForgeDesk.Presentation.Models;

namespace ForgeDesk.Presentation.Controls;

/// <summary>
/// Lightweight retained WPF drawing surface for a bounded dependency graph.
/// Nodes and edges remain data-bound; the control only handles viewport and hit
/// testing so the graph does not create one WPF control per persisted symbol.
/// </summary>
public sealed class DependencyGraphCanvas : FrameworkElement
{
    private const double NodeWidth = 148;
    private const double NodeHeight = 52;
    private const double MinimumZoom = 0.2;
    private const double MaximumZoom = 2.5;
    private Point _lastMousePoint;
    private bool _isPanning;
    private bool _didPan;

    public DependencyGraphCanvas()
    {
        Focusable = true;
        SnapsToDevicePixels = true;
        ClipToBounds = true;
        MouseMove += OnMouseMove;
        MouseLeftButtonDown += OnMouseLeftButtonDown;
        MouseLeftButtonUp += OnMouseLeftButtonUp;
        MouseLeave += OnMouseLeave;
        MouseWheel += OnMouseWheel;
    }

    public static readonly DependencyProperty ProjectionProperty = DependencyProperty.Register(
        nameof(Projection),
        typeof(DependencyGraphProjection),
        typeof(DependencyGraphCanvas),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SelectedNodeIdProperty = DependencyProperty.Register(
        nameof(SelectedNodeId),
        typeof(Guid?),
        typeof(DependencyGraphCanvas),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SelectedEdgeIdProperty = DependencyProperty.Register(
        nameof(SelectedEdgeId),
        typeof(Guid?),
        typeof(DependencyGraphCanvas),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ZoomProperty = DependencyProperty.Register(
        nameof(Zoom),
        typeof(double),
        typeof(DependencyGraphCanvas),
        new FrameworkPropertyMetadata(1d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PanXProperty = DependencyProperty.Register(
        nameof(PanX),
        typeof(double),
        typeof(DependencyGraphCanvas),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PanYProperty = DependencyProperty.Register(
        nameof(PanY),
        typeof(double),
        typeof(DependencyGraphCanvas),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FitRequestProperty = DependencyProperty.Register(
        nameof(FitRequest),
        typeof(int),
        typeof(DependencyGraphCanvas),
        new PropertyMetadata(0, OnFitRequestChanged));

    public static readonly DependencyProperty ResetRequestProperty = DependencyProperty.Register(
        nameof(ResetRequest),
        typeof(int),
        typeof(DependencyGraphCanvas),
        new PropertyMetadata(0, OnResetRequestChanged));

    public static readonly DependencyProperty NodeBrushProperty = DependencyProperty.Register(
        nameof(NodeBrush),
        typeof(Brush),
        typeof(DependencyGraphCanvas),
        new FrameworkPropertyMetadata(Brushes.SteelBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty NodeAlternateBrushProperty = DependencyProperty.Register(
        nameof(NodeAlternateBrush),
        typeof(Brush),
        typeof(DependencyGraphCanvas),
        new FrameworkPropertyMetadata(Brushes.DarkSlateBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty NodeMemberBrushProperty = DependencyProperty.Register(
        nameof(NodeMemberBrush),
        typeof(Brush),
        typeof(DependencyGraphCanvas),
        new FrameworkPropertyMetadata(Brushes.DarkCyan, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty EdgeBrushProperty = DependencyProperty.Register(
        nameof(EdgeBrush),
        typeof(Brush),
        typeof(DependencyGraphCanvas),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SelectedBrushProperty = DependencyProperty.Register(
        nameof(SelectedBrush),
        typeof(Brush),
        typeof(DependencyGraphCanvas),
        new FrameworkPropertyMetadata(Brushes.Orange, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LabelBrushProperty = DependencyProperty.Register(
        nameof(LabelBrush),
        typeof(Brush),
        typeof(DependencyGraphCanvas),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BackgroundBrushProperty = DependencyProperty.Register(
        nameof(BackgroundBrush),
        typeof(Brush),
        typeof(DependencyGraphCanvas),
        new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

    public DependencyGraphProjection? Projection
    {
        get => (DependencyGraphProjection?)GetValue(ProjectionProperty);
        set => SetValue(ProjectionProperty, value);
    }

    public Guid? SelectedNodeId
    {
        get => (Guid?)GetValue(SelectedNodeIdProperty);
        set => SetValue(SelectedNodeIdProperty, value);
    }

    public Guid? SelectedEdgeId
    {
        get => (Guid?)GetValue(SelectedEdgeIdProperty);
        set => SetValue(SelectedEdgeIdProperty, value);
    }

    public double Zoom
    {
        get => (double)GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, Math.Clamp(value, MinimumZoom, MaximumZoom));
    }

    public double PanX
    {
        get => (double)GetValue(PanXProperty);
        set => SetValue(PanXProperty, value);
    }

    public double PanY
    {
        get => (double)GetValue(PanYProperty);
        set => SetValue(PanYProperty, value);
    }

    public int FitRequest
    {
        get => (int)GetValue(FitRequestProperty);
        set => SetValue(FitRequestProperty, value);
    }

    public int ResetRequest
    {
        get => (int)GetValue(ResetRequestProperty);
        set => SetValue(ResetRequestProperty, value);
    }

    public Brush NodeBrush
    {
        get => (Brush)GetValue(NodeBrushProperty);
        set => SetValue(NodeBrushProperty, value);
    }

    public Brush NodeAlternateBrush
    {
        get => (Brush)GetValue(NodeAlternateBrushProperty);
        set => SetValue(NodeAlternateBrushProperty, value);
    }

    public Brush NodeMemberBrush
    {
        get => (Brush)GetValue(NodeMemberBrushProperty);
        set => SetValue(NodeMemberBrushProperty, value);
    }

    public Brush EdgeBrush
    {
        get => (Brush)GetValue(EdgeBrushProperty);
        set => SetValue(EdgeBrushProperty, value);
    }

    public Brush SelectedBrush
    {
        get => (Brush)GetValue(SelectedBrushProperty);
        set => SetValue(SelectedBrushProperty, value);
    }

    public Brush LabelBrush
    {
        get => (Brush)GetValue(LabelBrushProperty);
        set => SetValue(LabelBrushProperty, value);
    }

    public Brush BackgroundBrush
    {
        get => (Brush)GetValue(BackgroundBrushProperty);
        set => SetValue(BackgroundBrushProperty, value);
    }

    public event EventHandler<GraphNodeSelectedEventArgs>? NodeSelected;

    public event EventHandler<GraphEdgeSelectedEventArgs>? EdgeSelected;

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        drawingContext.DrawRectangle(BackgroundBrush, null, new Rect(0, 0, ActualWidth, ActualHeight));
        if (Projection is null)
        {
            return;
        }

        var nodeById = Projection.Nodes.ToDictionary(node => node.Id);
        foreach (var edge in Projection.Edges)
        {
            if (!nodeById.TryGetValue(edge.FromId, out var from)
                || !nodeById.TryGetValue(edge.ToId, out var to))
            {
                continue;
            }

            DrawEdge(drawingContext, edge, from, to);
        }

        foreach (var node in Projection.Nodes)
        {
            DrawNode(drawingContext, node);
        }
    }

    private void DrawEdge(
        DrawingContext drawingContext,
        DependencyGraphEdgeItem edge,
        DependencyGraphNodeItem from,
        DependencyGraphNodeItem to)
    {
        var start = Transform(new Point(from.X, from.Y));
        var end = Transform(new Point(to.X, to.Y));
        var brush = edge.IsHighlighted || edge.Id == SelectedEdgeId ? SelectedBrush : EdgeBrush;
        var pen = new Pen(brush, edge.IsHighlighted || edge.Id == SelectedEdgeId ? 2.5 : 1.2);
        pen.FreezeIfPossible();

        if (edge.FromId == edge.ToId)
        {
            drawingContext.DrawEllipse(null, pen, new Point(start.X, start.Y - 28 * Zoom), 22 * Zoom, 18 * Zoom);
            DrawArrowHead(drawingContext, new Point(start.X + 20 * Zoom, start.Y - 30 * Zoom), new Vector(1, 0), brush);
            return;
        }

        var direction = end - start;
        if (direction.Length > 0.1)
        {
            direction.Normalize();
        }

        var startPoint = start + direction * (NodeWidth * Zoom / 2);
        var endPoint = end - direction * (NodeWidth * Zoom / 2);
        drawingContext.DrawLine(pen, startPoint, endPoint);
        DrawArrowHead(drawingContext, endPoint, direction, brush);
    }

    private void DrawNode(DrawingContext drawingContext, DependencyGraphNodeItem node)
    {
        var center = Transform(new Point(node.X, node.Y));
        var rect = new Rect(
            center.X - NodeWidth * Zoom / 2,
            center.Y - NodeHeight * Zoom / 2,
            NodeWidth * Zoom,
            NodeHeight * Zoom);
        var fill = node.IsSelected
            ? SelectedBrush
            : node.Kind is SymbolKind.Method or SymbolKind.Property or SymbolKind.Field or SymbolKind.Event
                ? NodeMemberBrush
                : node.Kind is SymbolKind.Interface or SymbolKind.Record
                    ? NodeAlternateBrush
                    : NodeBrush;
        var border = node.IsSelected || node.IsNeighbor ? SelectedBrush : EdgeBrush;
        var pen = new Pen(border, node.IsSelected ? 2.5 : 1);
        pen.FreezeIfPossible();
        drawingContext.DrawRoundedRectangle(fill, pen, rect, 8, 8);

        if (Zoom < 0.42)
        {
            return;
        }

        var label = node.DisplayName.Length > 24 ? node.DisplayName[..21] + "..." : node.DisplayName;
        var kind = node.KindLabel;
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var text = new FormattedText(label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI Semibold"), 12 * Zoom, LabelBrush, dpi);
        var kindText = new FormattedText(kind, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 9 * Zoom, LabelBrush, dpi);
        drawingContext.DrawText(text, new Point(center.X - text.Width / 2, center.Y - 15 * Zoom));
        drawingContext.DrawText(kindText, new Point(center.X - kindText.Width / 2, center.Y + 2 * Zoom));
    }

    private void DrawArrowHead(DrawingContext drawingContext, Point tip, Vector direction, Brush brush)
    {
        if (direction.Length < 0.1)
        {
            return;
        }

        direction.Normalize();
        var side = new Vector(-direction.Y, direction.X);
        var left = tip - direction * (8 * Zoom) + side * (4 * Zoom);
        var right = tip - direction * (8 * Zoom) - side * (4 * Zoom);
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(tip, true, true);
            context.LineTo(left, true, false);
            context.LineTo(right, true, false);
        }

        geometry.Freeze();
        drawingContext.DrawGeometry(brush, null, geometry);
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs args)
    {
        Focus();
        _lastMousePoint = args.GetPosition(this);
        _didPan = false;
        _isPanning = HitTestNode(_lastMousePoint) is null && HitTestEdge(_lastMousePoint) is null;
        if (_isPanning)
        {
            CaptureMouse();
            Cursor = Cursors.Hand;
        }
        else
        {
            SelectAt(_lastMousePoint);
        }

        args.Handled = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs args)
    {
        var point = args.GetPosition(this);
        if (_isPanning && args.LeftButton == MouseButtonState.Pressed)
        {
            var delta = point - _lastMousePoint;
            if (delta.Length > 0.5)
            {
                _didPan = true;
                PanX += delta.X;
                PanY += delta.Y;
                _lastMousePoint = point;
            }

            return;
        }

        var node = HitTestNode(point);
        var edge = node is null ? HitTestEdge(point) : null;
        ToolTip = node is not null
            ? $"{node.DisplayName} · {node.KindLabel}\n{node.LocationLabel}"
            : edge is not null
                ? $"{edge.RelationshipType}: {edge.FromName} → {edge.ToName}"
                : null;
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs args)
    {
        if (_isPanning)
        {
            ReleaseMouseCapture();
            Cursor = Cursors.Arrow;
            _isPanning = false;
        }

        if (!_didPan && HitTestNode(args.GetPosition(this)) is null && HitTestEdge(args.GetPosition(this)) is null)
        {
            SelectedNodeId = null;
            SelectedEdgeId = null;
        }

        args.Handled = true;
    }

    private void OnMouseLeave(object sender, MouseEventArgs args)
    {
        if (!_isPanning)
        {
            ToolTip = null;
        }
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs args)
    {
        var oldZoom = Zoom;
        var factor = args.Delta > 0 ? 1.12 : 1 / 1.12;
        var nextZoom = Math.Clamp(oldZoom * factor, MinimumZoom, MaximumZoom);
        var pointer = args.GetPosition(this);
        var graphPoint = new Point((pointer.X - PanX) / oldZoom, (pointer.Y - PanY) / oldZoom);
        Zoom = nextZoom;
        PanX = pointer.X - graphPoint.X * nextZoom;
        PanY = pointer.Y - graphPoint.Y * nextZoom;
        args.Handled = true;
    }

    private void SelectAt(Point point)
    {
        var node = HitTestNode(point);
        if (node is not null)
        {
            SelectedEdgeId = null;
            SelectedNodeId = node.Id;
            NodeSelected?.Invoke(this, new GraphNodeSelectedEventArgs(node.Id));
            return;
        }

        var edge = HitTestEdge(point);
        if (edge is not null)
        {
            SelectedNodeId = null;
            SelectedEdgeId = edge.Id;
            EdgeSelected?.Invoke(this, new GraphEdgeSelectedEventArgs(edge.Id));
        }
    }

    private DependencyGraphNodeItem? HitTestNode(Point point)
    {
        var graphPoint = ToGraphPoint(point);
        return Projection?.Nodes
            .Where(node => Math.Abs(graphPoint.X - node.X) <= NodeWidth / 2
                && Math.Abs(graphPoint.Y - node.Y) <= NodeHeight / 2)
            .OrderByDescending(node => node.IsSelected)
            .FirstOrDefault();
    }

    private DependencyGraphEdgeItem? HitTestEdge(Point point)
    {
        if (Projection is null)
        {
            return null;
        }

        var nodes = Projection.Nodes.ToDictionary(node => node.Id);
        var tolerance = 8d;
        foreach (var edge in Projection.Edges)
        {
            if (!nodes.TryGetValue(edge.FromId, out var from) || !nodes.TryGetValue(edge.ToId, out var to))
            {
                continue;
            }

            var start = Transform(new Point(from.X, from.Y));
            var end = Transform(new Point(to.X, to.Y));
            if (edge.FromId == edge.ToId)
            {
                var loopCenter = new Point(start.X, start.Y - 28 * Zoom);
                if (Math.Abs(point.X - loopCenter.X) <= 26 * Zoom && Math.Abs(point.Y - loopCenter.Y) <= 24 * Zoom)
                {
                    return edge;
                }
            }
            else if (DistanceToSegment(point, start, end) <= tolerance)
            {
                return edge;
            }
        }

        return null;
    }

    private Point Transform(Point point) => new(point.X * Zoom + PanX, point.Y * Zoom + PanY);

    private Point ToGraphPoint(Point point) => new((point.X - PanX) / Zoom, (point.Y - PanY) / Zoom);

    private static double DistanceToSegment(Point point, Point start, Point end)
    {
        var delta = end - start;
        if (delta.LengthSquared < 0.01)
        {
            return (point - start).Length;
        }

        var t = ((point.X - start.X) * delta.X + (point.Y - start.Y) * delta.Y) / delta.LengthSquared;
        t = Math.Clamp(t, 0, 1);
        var projection = start + delta * t;
        return (point - projection).Length;
    }

    private void FitToView()
    {
        if (Projection?.Nodes.Count is not > 0 || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var minX = Projection.Nodes.Min(node => node.X) - NodeWidth / 2;
        var maxX = Projection.Nodes.Max(node => node.X) + NodeWidth / 2;
        var minY = Projection.Nodes.Min(node => node.Y) - NodeHeight / 2;
        var maxY = Projection.Nodes.Max(node => node.Y) + NodeHeight / 2;
        var graphWidth = Math.Max(1, maxX - minX);
        var graphHeight = Math.Max(1, maxY - minY);
        var nextZoom = Math.Clamp(Math.Min((ActualWidth - 32) / graphWidth, (ActualHeight - 32) / graphHeight), MinimumZoom, MaximumZoom);
        Zoom = nextZoom;
        PanX = (ActualWidth - graphWidth * nextZoom) / 2 - minX * nextZoom;
        PanY = (ActualHeight - graphHeight * nextZoom) / 2 - minY * nextZoom;
    }

    private static void OnFitRequestChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
        => ((DependencyGraphCanvas)dependencyObject).FitToView();

    private static void OnResetRequestChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        var canvas = (DependencyGraphCanvas)dependencyObject;
        canvas.Zoom = 1;
        canvas.PanX = 0;
        canvas.PanY = 0;
    }
}

public sealed class GraphNodeSelectedEventArgs : EventArgs
{
    public GraphNodeSelectedEventArgs(Guid nodeId) => NodeId = nodeId;

    public Guid NodeId { get; }
}

public sealed class GraphEdgeSelectedEventArgs : EventArgs
{
    public GraphEdgeSelectedEventArgs(Guid edgeId) => EdgeId = edgeId;

    public Guid EdgeId { get; }
}

internal static class PenExtensions
{
    public static void FreezeIfPossible(this Pen pen)
    {
        if (pen.CanFreeze)
        {
            pen.Freeze();
        }
    }
}
