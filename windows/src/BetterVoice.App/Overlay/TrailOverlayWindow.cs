using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using BetterVoice.App.Native;
using BetterVoice.Core;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;

namespace BetterVoice.App.Overlay;

public sealed class TrailOverlayWindow : Window
{
    private readonly List<PointD> _points = [];
    private readonly List<double> _times = [];
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private CircleGesture? _highlightGesture;
    private double _highlightUntil;

    private readonly Pen _trailPen;
    private readonly Pen _circlePen;
    private readonly Brush _circleFill;

    public TrailOverlayWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        ShowInTaskbar = false;
        Focusable = false;
        IsHitTestVisible = false;
        Topmost = true;

        var trailBrush = new SolidColorBrush(Color.FromArgb(200, 0, 160, 255));
        trailBrush.Freeze();
        _trailPen = new Pen(trailBrush, 3.5);
        _trailPen.Freeze();

        var circleStroke = new SolidColorBrush(Color.FromArgb(230, 0, 122, 255));
        circleStroke.Freeze();
        _circlePen = new Pen(circleStroke, 4.0);
        _circlePen.Freeze();

        var fillBrush = new SolidColorBrush(Color.FromArgb(40, 0, 180, 255));
        fillBrush.Freeze();
        _circleFill = fillBrush;

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var helper = new WindowInteropHelper(this);
        IntPtr hwnd = helper.Handle;

        // Cover entire virtual desktop
        int vx = Win32Api.GetSystemMetrics(Win32Api.SM_XVIRTUALSCREEN);
        int vy = Win32Api.GetSystemMetrics(Win32Api.SM_YVIRTUALSCREEN);
        int vw = Win32Api.GetSystemMetrics(Win32Api.SM_CXVIRTUALSCREEN);
        int vh = Win32Api.GetSystemMetrics(Win32Api.SM_CYVIRTUALSCREEN);

        Left = vx;
        Top = vy;
        Width = vw;
        Height = vh;

        // Set layered, transparent, click-through, non-activating window flags
        int exStyle = (int)Win32Api.GetWindowLongPtr(hwnd, Win32Api.GWL_EXSTYLE);
        exStyle |= Win32Api.WS_EX_LAYERED | Win32Api.WS_EX_TRANSPARENT | Win32Api.WS_EX_NOACTIVATE | Win32Api.WS_EX_TOOLWINDOW | Win32Api.WS_EX_TOPMOST;
        Win32Api.SetWindowLongPtr(hwnd, Win32Api.GWL_EXSTYLE, (IntPtr)exStyle);

        // Exclude this overlay window from any screen capture / screenshots!
        Win32Api.SetWindowDisplayAffinity(hwnd, Win32Api.WDA_EXCLUDEFROMCAPTURE);

        CompositionTarget.Rendering += OnRendering;
    }

    public void AddPoint(PointD point)
    {
        double now = _stopwatch.Elapsed.TotalSeconds;
        _points.Add(point);
        _times.Add(now);
    }

    public void HighlightCircle(CircleGesture gesture)
    {
        _highlightGesture = gesture;
        _highlightUntil = _stopwatch.Elapsed.TotalSeconds + 0.6;
    }

    public void ClearTrail()
    {
        _points.Clear();
        _times.Clear();
        _highlightGesture = null;
        InvalidateVisual();
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        double now = _stopwatch.Elapsed.TotalSeconds;

        // Expire points older than 0.35 seconds
        double cutoff = now - 0.35;
        int removeCount = 0;
        for (int i = 0; i < _times.Count; i++)
        {
            if (_times[i] < cutoff) removeCount++;
            else break;
        }

        if (removeCount > 0)
        {
            _points.RemoveRange(0, removeCount);
            _times.RemoveRange(0, removeCount);
        }

        if (_highlightGesture != null && now > _highlightUntil)
        {
            _highlightGesture = null;
        }

        if (_points.Count > 0 || _highlightGesture != null)
        {
            InvalidateVisual();
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        double now = _stopwatch.Elapsed.TotalSeconds;

        // Draw gesture circle if active
        if (_highlightGesture is { } gesture)
        {
            var center = new Point(gesture.Center.X - Left, gesture.Center.Y - Top);
            dc.DrawEllipse(_circleFill, _circlePen, center, gesture.Radius, gesture.Radius);
        }

        if (_points.Count < 2) return;

        var segments = TrailSegments.Calculate(_points, _times);
        foreach (var seg in segments)
        {
            var p1 = new Point(_points[seg.From].X - Left, _points[seg.From].Y - Top);
            var p2 = new Point(_points[seg.To].X - Left, _points[seg.To].Y - Top);

            // Compute fading alpha based on age
            double age = now - _times[seg.To];
            double alphaRatio = Math.Max(0, 1.0 - (age / 0.35));
            byte alpha = (byte)(alphaRatio * 210);

            var pen = new Pen(new SolidColorBrush(Color.FromArgb(alpha, 0, 170, 255)), 3.0);
            pen.Freeze();
            dc.DrawLine(pen, p1, p2);
        }
    }
}
