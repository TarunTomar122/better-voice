using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using BetterVoice.Core;

namespace BetterVoice.App.Native;

public static class ScreenshotCapture
{
    private const int SRCCOPY = 0x00CC0020;
    private const int CAPTUREBLT = 0x40000000;

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hDC, int nWidth, int nHeight);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight, IntPtr hdcSrc, int nXSrc, int nYSrc, int dwRop);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    public static void Capture(CircleGesture gesture, string destinationPath)
    {
        var targetPoint = new Point((int)Math.Round(gesture.Center.X), (int)Math.Round(gesture.Center.Y));
        var screen = Screen.FromPoint(targetPoint);
        var bounds = screen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);

        int width = Math.Max(100, bounds.Width);
        int height = Math.Max(100, bounds.Height);

        using var bitmap = CaptureScreenBitmap(bounds.X, bounds.Y, width, height);
        using (var g = Graphics.FromImage(bitmap))
        {
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

    private static Bitmap CaptureScreenBitmap(int x, int y, int width, int height)
    {
        IntPtr hDesk = GetDC(IntPtr.Zero);
        if (hDesk != IntPtr.Zero)
        {
            try
            {
                IntPtr hMemDC = CreateCompatibleDC(hDesk);
                if (hMemDC != IntPtr.Zero)
                {
                    try
                    {
                        IntPtr hBmp = CreateCompatibleBitmap(hDesk, width, height);
                        if (hBmp != IntPtr.Zero)
                        {
                            try
                            {
                                IntPtr hOld = SelectObject(hMemDC, hBmp);
                                bool success = BitBlt(hMemDC, 0, 0, width, height, hDesk, x, y, SRCCOPY | CAPTUREBLT);
                                SelectObject(hMemDC, hOld);

                                if (success)
                                {
                                    using var temp = Image.FromHbitmap(hBmp);
                                    return new Bitmap(temp);
                                }
                            }
                            finally
                            {
                                DeleteObject(hBmp);
                            }
                        }
                    }
                    finally
                    {
                        DeleteDC(hMemDC);
                    }
                }
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, hDesk);
            }
        }

        // Fallback placeholder bitmap if screen DC is unavailable in current session
        var fallback = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(fallback))
        {
            g.Clear(Color.FromArgb(30, 30, 35));
        }
        return fallback;
    }
}
