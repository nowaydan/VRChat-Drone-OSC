using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VRCDroneOSC.Services;

namespace VRCDroneOSC.Controls;

/// <summary>
/// Visualises a Betaflight-style rate curve.
/// Plots RateCurve.Apply output (0 to MaxRate) against stick input (0 to 1).
/// A red dot marks the current input position.
/// </summary>
public class RateCurveGraph : Control
{
    private static readonly SolidColorBrush BgBrush =
        new(Color.FromRgb(0x16, 0x21, 0x3E));

    private static readonly Pen GridPen =
        new(new SolidColorBrush(Color.FromArgb(0x55, 0x94, 0xA3, 0xB8)), 1.0);

    private static readonly Pen CurvePen =
        new(new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6)), 2.0);

    private static readonly SolidColorBrush DotBrush =
        new(Color.FromRgb(0xEF, 0x44, 0x44));

    private static readonly Pen BorderPen =
        new(new SolidColorBrush(Color.FromRgb(0x2D, 0x3A, 0x52)), 1.0);

    // DependencyProperties
    public static readonly DependencyProperty CenterProperty =
        DependencyProperty.Register(nameof(Center), typeof(double), typeof(RateCurveGraph),
            new FrameworkPropertyMetadata(100.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaxRateProperty =
        DependencyProperty.Register(nameof(MaxRate), typeof(double), typeof(RateCurveGraph),
            new FrameworkPropertyMetadata(500.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ExpoProperty =
        DependencyProperty.Register(nameof(Expo), typeof(double), typeof(RateCurveGraph),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CurrentInputProperty =
        DependencyProperty.Register(nameof(CurrentInput), typeof(double), typeof(RateCurveGraph),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Center
    {
        get => (double)GetValue(CenterProperty);
        set => SetValue(CenterProperty, value);
    }

    public double MaxRate
    {
        get => (double)GetValue(MaxRateProperty);
        set => SetValue(MaxRateProperty, value);
    }

    public double Expo
    {
        get => (double)GetValue(ExpoProperty);
        set => SetValue(ExpoProperty, value);
    }

    /// <summary>Input position in the range [0, 1].</summary>
    public double CurrentInput
    {
        get => (double)GetValue(CurrentInputProperty);
        set => SetValue(CurrentInputProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth;
        double h = ActualHeight;
        const int padding = 4;

        double plotW = w - padding * 2;
        double plotH = h - padding * 2;

        // Background
        dc.DrawRectangle(BgBrush, null, new Rect(0, 0, w, h));

        // Grid lines (quarters)
        for (int i = 1; i < 4; i++)
        {
            double gx = padding + (plotW * i / 4.0);
            double gy = padding + (plotH * i / 4.0);
            dc.DrawLine(GridPen, new Point(gx, padding), new Point(gx, padding + plotH));
            dc.DrawLine(GridPen, new Point(padding, gy), new Point(padding + plotW, gy));
        }

        // Blue curve — plot RateCurve.Apply from input 0..1 using positive side
        double maxRate = MaxRate > 0 ? MaxRate : 1.0;
        const int steps = 80;
        Point? prev = null;
        for (int i = 0; i <= steps; i++)
        {
            double t = i / (double)steps; // stick input 0..1
            double rate = RateCurve.Apply(t, Center, MaxRate, Expo);
            double norm = maxRate > 0 ? rate / maxRate : 0;
            norm = Math.Clamp(norm, 0.0, 1.0);

            double px = padding + t * plotW;
            double py = padding + (1.0 - norm) * plotH;
            var pt = new Point(px, py);

            if (prev.HasValue)
                dc.DrawLine(CurvePen, prev.Value, pt);
            prev = pt;
        }

        // Red dot at current input position
        double ci = Math.Clamp(CurrentInput, 0.0, 1.0);
        double ciRate = RateCurve.Apply(ci, Center, MaxRate, Expo);
        double ciNorm = Math.Clamp(maxRate > 0 ? ciRate / maxRate : 0, 0.0, 1.0);
        double dotX = padding + ci * plotW;
        double dotY = padding + (1.0 - ciNorm) * plotH;
        dc.DrawEllipse(DotBrush, null, new Point(dotX, dotY), 5, 5);

        // Border
        dc.DrawRectangle(null, BorderPen, new Rect(0.5, 0.5, w - 1, h - 1));
    }
}
