using System;
using System.Drawing;
using System.Windows.Forms;
using BetterVoice.Core;

namespace BetterVoice.App.UI;

public sealed class TrayIconManager : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly AppController _controller;

    public TrayIconManager(AppController controller)
    {
        _controller = controller;

        _notifyIcon = new NotifyIcon
        {
            Text = "BetterVoice - Local Voice Dictation",
            Visible = true,
            Icon = CreateAppIcon()
        };

        var menu = new ContextMenuStrip();

        var headerItem = new ToolStripMenuItem("BetterVoice (Ready)") { Enabled = false };
        headerItem.Font = new Font(headerItem.Font, FontStyle.Bold);
        menu.Items.Add(headerItem);
        menu.Items.Add(new ToolStripSeparator());

        var settingsItem = new ToolStripMenuItem("Settings...", null, (s, e) => _controller.ShowSettings());
        menu.Items.Add(settingsItem);

        var vocabItem = new ToolStripMenuItem("Open Custom Vocabulary...", null, (s, e) =>
        {
            string path = VocabularyFile.DefaultPath();
            VocabularyFile.CreateTemplateIfMissing(path);
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch { }
        });
        menu.Items.Add(vocabItem);

        menu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem("Exit BetterVoice", null, (s, e) =>
        {
            _notifyIcon.Visible = false;
            System.Windows.Application.Current.Shutdown();
        });
        menu.Items.Add(exitItem);

        _notifyIcon.ContextMenuStrip = menu;
        _notifyIcon.DoubleClick += (s, e) => _controller.ShowSettings();
    }

    private static Icon CreateAppIcon()
    {
        // Generate a crisp, modern vector-style 32x32 tray icon programmatically
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            // Blue circular badge
            using var brush = new SolidBrush(Color.FromArgb(2, 132, 199));
            g.FillEllipse(brush, 2, 2, 28, 28);

            // White BV letters
            using var font = new Font("Segoe UI", 11, FontStyle.Bold);
            using var textBrush = new SolidBrush(Color.White);
            var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
            g.DrawString("V", font, textBrush, new RectangleF(0, 0, 32, 32), format);
        }

        IntPtr hIcon = bitmap.GetHicon();
        return Icon.FromHandle(hIcon);
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
