using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace BetterVoice.Core;

public readonly record struct PointD(double X, double Y);

public readonly record struct CircleGesture(PointD Center, double Radius);

/// <summary>
/// Detects one closed, roughly circular mouse stroke at a time.
/// Uses a small rolling path and geometric checks.
/// Highly optimized with zero-allocation stackalloc buffers and Span slices.
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

    private double _minimumAngleDegrees;

    public double MinimumAngleDegrees => Volatile.Read(ref _minimumAngleDegrees);

    public CircleGestureDetector(double minimumAngleDegrees = 340)
    {
        SetMinimumAngleDegrees(minimumAngleDegrees);
    }

    public void SetMinimumAngleDegrees(double minimumAngleDegrees)
    {
        Volatile.Write(ref _minimumAngleDegrees, Math.Clamp(minimumAngleDegrees, 300, 359));
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

        int removeCount = 0;
        while (removeCount < _samples.Count && _samples[removeCount].Time < cutoff)
        {
            removeCount++;
        }
        if (removeCount > 0)
        {
            _samples.RemoveRange(0, removeCount);
        }

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
        int count = _samples.Count;
        if (count == 0)
        {
            return null;
        }

        var span = CollectionsMarshal.AsSpan(_samples);
        PointD last = span[count - 1].Point;

        for (int start = count - 18; start >= 0; start--)
        {
            PointD first = span[start].Point;
            if (Hypot(first.X - last.X, first.Y - last.Y) >= 160)
            {
                continue;
            }

            var subSpan = span.Slice(start, count - start);
            if (RecognizedGesture(subSpan) is { } gesture)
            {
                return gesture;
            }
        }

        return null;
    }

    private CircleGesture? RecognizedGesture(ReadOnlySpan<Sample> samples)
    {
        if (samples.Length == 0) return null;
        PointD first = samples[0].Point;
        PointD last = samples[^1].Point;

        double minX = double.MaxValue, maxX = double.MinValue;
        double minY = double.MaxValue, maxY = double.MinValue;

        for (int i = 0; i < samples.Length; i++)
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
        Span<double> distances = samples.Length <= 512 ? stackalloc double[samples.Length] : new double[samples.Length];

        for (int i = 0; i < samples.Length; i++)
        {
            double d = Hypot(samples[i].Point.X - center.X, samples[i].Point.Y - center.Y);
            distances[i] = d;
            distSum += d;
        }

        double meanRadius = distSum / samples.Length;
        if (meanRadius < 18) return null;

        double varianceSum = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            double diff = distances[i] - meanRadius;
            varianceSum += diff * diff;
        }

        double stdDev = Math.Sqrt(varianceSum / samples.Length);
        if (stdDev / meanRadius >= 0.32) return null;

        double closureDistance = Hypot(first.X - last.X, first.Y - last.Y);
        if (closureDistance >= Math.Max(20.0, meanRadius * 0.65)) return null;

        // Reject sharp polygonal corners (e.g. triangles or rectangles)
        for (int i = 2; i < samples.Length; i++)
        {
            var p0 = samples[i - 2].Point;
            var p1 = samples[i - 1].Point;
            var p2 = samples[i].Point;

            double dx1 = p1.X - p0.X;
            double dy1 = p1.Y - p0.Y;
            double dx2 = p2.X - p1.X;
            double dy2 = p2.Y - p1.Y;

            if (Hypot(dx1, dy1) >= 2.0 && Hypot(dx2, dy2) >= 2.0)
            {
                double h1 = Math.Atan2(dy1, dx1);
                double h2 = Math.Atan2(dy2, dx2);
                double dh = Math.Abs(h2 - h1);
                while (dh > Math.PI) dh = 2.0 * Math.PI - dh;

                if (dh > 1.3)
                {
                    return null;
                }
            }
        }

        double angleTravel = 0;
        for (int i = 1; i < samples.Length; i++)
        {
            var pPrev = samples[i - 1].Point;
            var pCurr = samples[i].Point;

            double a1 = Math.Atan2(pPrev.Y - center.Y, pPrev.X - center.X);
            double a2 = Math.Atan2(pCurr.Y - center.Y, pCurr.X - center.X);

            double delta = a2 - a1;
            while (delta > Math.PI) delta -= 2.0 * Math.PI;
            while (delta < -Math.PI) delta += 2.0 * Math.PI;

            angleTravel += Math.Abs(delta);
        }

        double minAngleRad = MinimumAngleDegrees * Math.PI / 180.0;
        if (angleTravel <= minAngleRad || angleTravel >= 8.8) return null;

        double pathLength = 0;
        for (int i = 1; i < samples.Length; i++)
        {
            pathLength += Hypot(samples[i].Point.X - samples[i - 1].Point.X, samples[i].Point.Y - samples[i - 1].Point.Y);
        }

        double circumference = 2.0 * Math.PI * meanRadius;
        double lengthRatio = pathLength / circumference;
        if (lengthRatio <= 0.65 || lengthRatio >= 1.9) return null;

        return new CircleGesture(center, meanRadius);
    }
}
