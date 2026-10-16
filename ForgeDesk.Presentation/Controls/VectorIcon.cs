using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ForgeDesk.Presentation.Controls;

/// <summary>Draws a geometry resource without depending on a font or external icon library.</summary>
public sealed class VectorIcon : FrameworkElement
{
    public static readonly DependencyProperty IconKeyProperty = DependencyProperty.Register(
        nameof(IconKey), typeof(string), typeof(VectorIcon),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForegroundProperty = Control.ForegroundProperty.AddOwner(
        typeof(VectorIcon), new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
        nameof(StrokeThickness), typeof(double), typeof(VectorIcon),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public string IconKey
    {
        get => (string)GetValue(IconKeyProperty);
        set => SetValue(IconKeyProperty, value);
    }

    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 18 : availableSize.Width;
        var height = double.IsInfinity(availableSize.Height) ? 18 : availableSize.Height;
        return new Size(Math.Max(0, width), Math.Max(0, height));
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (string.IsNullOrWhiteSpace(IconKey) || TryFindResource(IconKey) is not Geometry geometry)
        {
            return;
        }

        var bounds = geometry.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0 || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var scale = Math.Min(ActualWidth / bounds.Width, ActualHeight / bounds.Height);
        var offsetX = (ActualWidth - (bounds.Width * scale)) / 2d;
        var offsetY = (ActualHeight - (bounds.Height * scale)) / 2d;
        drawingContext.PushTransform(new TranslateTransform(offsetX, offsetY));
        drawingContext.PushTransform(new ScaleTransform(scale, scale));
        drawingContext.PushTransform(new TranslateTransform(-bounds.X, -bounds.Y));
        var pen = StrokeThickness > 0 ? new Pen(Foreground, StrokeThickness / scale) : null;
        drawingContext.DrawGeometry(Foreground, pen, geometry);
        drawingContext.Pop();
        drawingContext.Pop();
        drawingContext.Pop();
    }
}
