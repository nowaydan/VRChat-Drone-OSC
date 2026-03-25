using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace VRCDroneOSC.Controls;

/// <summary>
/// Vertical bar showing throttle percentage (0–1).
/// </summary>
public class ThrottleBar : Control
{
    private static readonly SolidColorBrush BgBrush =
        new(Color.FromRgb(0x16, 0x21, 0x3E));

    private static readonly SolidColorBrush FillBrush =
        new(Color.FromRgb(0x3B, 0x82, 0xF6));

    private static readonly Pen BorderPen =
        new(new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6)), 1.5);

    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(
            nameof(Value), typeof(double), typeof(ThrottleBar),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth;
        double h = ActualHeight;
        double clamped = Math.Clamp(Value, 0.0, 1.0);

        // Dark background
        dc.DrawRectangle(BgBrush, null, new Rect(0, 0, w, h));

        // Blue fill from bottom, proportional to value
        double fillH = h * clamped;
        if (fillH > 0)
            dc.DrawRectangle(FillBrush, null, new Rect(0, h - fillH, w, fillH));

        // Border
        dc.DrawRectangle(null, BorderPen, new Rect(0.75, 0.75, w - 1.5, h - 1.5));

        // Percentage text centered
        int pct = (int)Math.Round(clamped * 100);
        var ft = new FormattedText(
            $"{pct}%",
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            12,
            new SolidColorBrush(Colors.White),
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

        dc.DrawText(ft, new Point((w - ft.Width) / 2, (h - ft.Height) / 2));
    }
}
