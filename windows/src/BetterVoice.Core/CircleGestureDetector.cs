using System;
using System.Collections.Generic;

namespace BetterVoice.Core;

public readonly record struct PointD(double X, double Y);

public readonly record struct CircleGesture(PointD Center, double Radius);

/// <summary>
/// Detects one closed, roughly circular mouse stroke at a time.
/// Uses a small rolling path and geometric checks.
/// </summary>
public sealed class CircleGestureDetector
{
    private static double Hypot(double x, double y) => Math.Sqrt(x * x + y * y);

    private readonly struct Sample
    {
        public PointD Point { get; }
        public double Time { get; }

        public Sample(PointD point, double time)
        {
            Point = point;
            Time = time;
        }
    }

    private readonly List<Sample> _samples = [];
    private double _cooldownUntil = 0;
    private CircleGesture? _waitingForExit = null;
    private const double Window = 6.0;

    public double MinimumAngleDegrees { get; }

    public CircleGestureDetector(double minimumAngleDegrees = 340)
    {
        MinimumAngleDegrees = Math.Min(Math.Max(minimumAngleDegrees, 300), 359);
    }

    public void Reset()
    {
        _samples.Clear();
        _cooldownUntil = 0;
        _waitingForExit = null;
    }

    public CircleGesture? Add(PointD point, double time)
    {
        if (_waitingForExit is { } gesture)
        {
            if (Hypot(point.X - gesture.Center.X, point.Y - gesture.Center.Y) > gesture.Radius * 1.5)
            {
                _waitingForExit = null;
                _samples.Clear();
            }
            else
            {
                return null;
            }
        }

        if (time < _cooldownUntil)
        {
            return null;
        }

        if (_samples.Count > 0 && (time - _samples[^1].Time) > 0.45)
        {
            _samples.Clear();
        }

        _samples.Add(new Sample(point, time));
        double cutoff = time - Window;
        _samples.RemoveAll(s => s.Time < cutoff);

        if (time < _cooldownUntil || _samples.Count < 18)
        {
            return null;
        }

        var recognized = RecognizedGesture();
        if (recognized is null)
        {
            return null;
        }

        _samples.Clear();
        _cooldownUntil = time + 0.65;
        _waitingForExit = recognized;
        return recognized;
    }

    private CircleGesture? RecognizedGesture()
    {
        if (_samples.Count == 0)
        {
            return null;
        }

        PointD last = _samples[^1].Point;
        for (int start = _samples.Count - 18; start >= 0; start--)
        {
            PointD first = _samples[start].Point;
            if (Hypot(first.X - last.X, first.Y - last.Y) >= 160)
            {
                continue;
            }

            var sublist = _samples.GetRange(start, _samples.Count - start);
            if (RecognizedGesture(sublist) is { } gesture)
            {
                return gesture;
            }
        }

        return null;
    }

    private CircleGesture? RecognizedGesture(List<Sample> samples)
    {
        if (samples.Count == 0) return null;
        PointD first = samples[0].Point;
        PointD last = samples[^1].Point;

        double minX = double.MaxValue, maxX = double.MinValue;
        double minY = double.MaxValue, maxY = double.MinValue;

        for (int i = 0; i < samples.Count; i++)
        {
            var p = samples[i].Point;
            if (p.X < minX) minX = p.X;
            if (p.X > maxX) maxX = p.X;
            if (p.Y < minY) minY = p.Y;
            if (p.Y > maxY) maxY = p.Y;
        }

        double width = maxX - minX;
        double height = maxY - minY;
        if (width < 28 || height < 28) return null;

        double aspect = width / height;
        if (aspect <= 0.45 || aspect >= 2.2) return null;

        PointD center = new((minX + maxX) / 2.0, (minY + maxY) / 2.0);

        double distSum = 0;
        var distances = new double[samples.Count];
        for (int i = 0; i < samples.Count; i++)
        {
            double d = Hypot(samples[i].Point.X - center.X, samples[i].Point.Y - center.Y);
            distances[i] = d;
            distSum += d;
        }

        double radius = distSum / samples.Count;
        if (radius < 18) return null;

        double varianceSum = 0;
        for (int i = 0; i < samples.Count; i++)
        {
            double diff = distances[i] - radius;
            varianceSum += diff * diff;
        }

        double variance = varianceSum / samples.Count;
        if (Math.Sqrt(variance) / radius >= 0.32) return null;

        double closure = Hypot(first.X - last.X, first.Y - last.Y);
        if (closure >= Math.Max(20, radius * 0.65)) return null;

        double angleTravel = 0;
        for (int i = 0; i < samples.Count - 1; i++)
        {
            var a = samples[i].Point;
            var b = samples[i + 1].Point;
            double current = Math.Atan2(a.Y - center.Y, a.X - center.X);
            double next = Math.Atan2(b.Y - center.Y, b.X - center.X);
            double delta = next - current;
            while (delta > Math.PI) delta -= 2 * Math.PI;
            while (delta < -Math.PI) delta += 2 * Math.PI;
            angleTravel += Math.Abs(delta);
        }

        double minimumAngle = MinimumAngleDegrees * Math.PI / 180.0;
        if (angleTravel <= minimumAngle || angleTravel >= 8.8) return null;

        double pathLength = 0;
        for (int i = 0; i < samples.Count - 1; i++)
        {
            pathLength += Hypot(samples[i + 1].Point.X - samples[i].Point.X, samples[i + 1].Point.Y - samples[i].Point.Y);
        }

        double circumference = 2 * Math.PI * radius;
        double pathRatio = pathLength / circumference;
        if (pathRatio <= 0.65 || pathRatio >= 1.9) return null;

        return new CircleGesture(center, radius);
    }
}
