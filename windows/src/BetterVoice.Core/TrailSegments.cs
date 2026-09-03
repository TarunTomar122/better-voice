using System;
using System.Collections.Generic;

namespace BetterVoice.Core;

public readonly record struct TrailSegment(int From, int To);

public static class TrailSegments
{
    /// <summary>
    /// Links only nearby samples so pauses and pointer jumps leave separate tail strokes.
    /// Highly optimized with squared distance checks (avoiding square root).
    /// </summary>
    public static List<TrailSegment> Calculate(
        IReadOnlyList<PointD> points,
        IReadOnlyList<double> times,
        double maximumGap = 0.18,
        double maximumDistance = 160.0)
    {
        int count = points.Count;
        if (count != times.Count || count <= 1)
        {
            return [];
        }

        double maxDistSq = maximumDistance * maximumDistance;
        var segments = new List<TrailSegment>(count);

        for (int index = 1; index < count; index++)
        {
            double gap = times[index] - times[index - 1];
            if (gap < 0 || gap > maximumGap) continue;

            double dx = points[index].X - points[index - 1].X;
            double dy = points[index].Y - points[index - 1].Y;
            double distSq = dx * dx + dy * dy;

            if (distSq <= maxDistSq)
            {
                segments.Add(new TrailSegment(index - 1, index));
            }
        }

        return segments;
    }
}
