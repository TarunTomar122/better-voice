using System;
using System.Collections.Generic;

namespace BetterVoice.Core;

public readonly record struct TrailSegment(int From, int To);

public static class TrailSegments
{
    private static double Hypot(double x, double y) => Math.Sqrt(x * x + y * y);

    /// <summary>
    /// Links only nearby samples so pauses and pointer jumps leave separate tail strokes.
    /// </summary>
    public static List<TrailSegment> Calculate(
        IReadOnlyList<PointD> points,
        IReadOnlyList<double> times,
        double maximumGap = 0.18,
        double maximumDistance = 160.0)
    {
        if (points.Count != times.Count || points.Count <= 1)
        {
            return [];
        }

        var segments = new List<TrailSegment>();
        for (int index = 1; index < points.Count; index++)
        {
            double gap = times[index] - times[index - 1];
            double distance = Hypot(
                points[index].X - points[index - 1].X,
                points[index].Y - points[index - 1].Y);

            if (gap >= 0 && gap <= maximumGap && distance <= maximumDistance)
            {
                segments.Add(new TrailSegment(index - 1, index));
            }
        }

        return segments;
    }
}
