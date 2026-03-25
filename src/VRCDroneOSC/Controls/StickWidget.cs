using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace VRCDroneOSC.Controls;

/// <summary>
/// Renders a circular joystick position indicator.
/// XValue and YValue are in the range [-1, 1].
/// </summary>
public class StickWidget : Control
{
    // #16213E background
    private static readonly SolidColorBrush BgBrush =
        new(Color.FromRgb(0x16, 0x21, 0x3E));

    // #3B82F6 accent (blue dot + border)
    private static readonly SolidColorBrush AccentBrush =
        new(Color.FromRgb(0x3B, 0x82, 0xF6));

    // #94A3B8 grid / crosshair
    private static readonly SolidColorBrush GridBrush =
        new(Color.FromRgb(0x94, 0xA3, 0xB8));

    private static readonly Pen CrosshairPen =
        new(new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)), 1.0);

    private static readonly Pen BorderRingPen =
        new(new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6)), 1.5);

    public static readonly DependencyProperty XValueProperty =
        DependencyProperty.Register(
            nameof(XValue), typeof(double), typeof(StickWidget),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty YValueProperty =
        DependencyProperty.Register(
            nameof(YValue), typeof(double), typeof(StickWidget),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public double XValue
    {
        get => (double)GetValue(XValueProperty);
        set => SetValue(XValueProperty, value);
    }

    public double YValue
    {
        get => (double)GetValue(YValueProperty);
        set => SetValue(YValueProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth;
        double h = ActualHeight;
        double radius = System.Math.Min(w, h) / 2.0 - 4;
        double cx = w / 2.0;
        double cy = h / 2.0;

        // Dark background circle
        dc.DrawEllipse(BgBrush, null, new Point(cx, cy), radius, radius);

        // Crosshair lines
        dc.DrawLine(CrosshairPen,
            new Point(cx - radius, cy),
            new Point(cx + radius, cy));
        dc.DrawLine(CrosshairPen,
            new Point(cx, cy - radius),
            new Point(cx, cy + radius));

        // Border ring
        dc.DrawEllipse(null, BorderRingPen, new Point(cx, cy), radius, radius);

        // Blue dot at stick position
        double dotX = cx + XValue * radius;
        double dotY = cy - YValue * radius; // Y-axis: up is positive
        double dotRadius = System.Math.Max(4, radius * 0.12);
        dc.DrawEllipse(AccentBrush, null, new Point(dotX, dotY), dotRadius, dotRadius);
    }
}
