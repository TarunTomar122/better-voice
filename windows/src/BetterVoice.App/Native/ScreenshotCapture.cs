using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using BetterVoice.Core;

namespace BetterVoice.App.Native;

public static class ScreenshotCapture
{
    public static void Capture(CircleGesture gesture, string destinationPath)
    {
        // Identify which screen contains the gesture center
        var targetPoint = new Point((int)Math.Round(gesture.Center.X), (int)Math.Round(gesture.Center.Y));
        var screen = Screen.FromPoint(targetPoint);
        var bounds = screen.Bounds;

        using var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);

            // Highlight circle
            float relX = (float)(gesture.Center.X - bounds.X);
            float relY = (float)(gesture.Center.Y - bounds.Y);
            float radius = (float)Math.Max(24.0, gesture.Radius);
            float haloRadius = radius * 1.35f;

            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Radial gradient halo
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(relX - haloRadius, relY - haloRadius, haloRadius * 2, haloRadius * 2);
                using var pgb = new PathGradientBrush(path)
                {
                    CenterPoint = new PointF(relX, relY),
                    CenterColor = Color.FromArgb(46, 0, 180, 255),
                    SurroundColors = [Color.FromArgb(0, 0, 120, 255)]
                };
                g.FillPath(pgb, path);
            }

            // Outer ring
            float strokeWidth = Math.Max(4f, radius * 0.055f);
            using var pen = new Pen(Color.FromArgb(230, 0, 122, 255), strokeWidth);
            g.DrawEllipse(pen, relX - radius, relY - radius, radius * 2, radius * 2);
        }

        string? dir = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        bitmap.Save(destinationPath, ImageFormat.Png);
    }
}
