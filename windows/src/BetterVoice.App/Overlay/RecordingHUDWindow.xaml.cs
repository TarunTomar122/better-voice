using System;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Media;
using BetterVoice.App.Native;

namespace BetterVoice.App.Overlay;

public partial class RecordingHUDWindow : Window
{
    public RecordingHUDWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var helper = new WindowInteropHelper(this);
        IntPtr hwnd = helper.Handle;

        int exStyle = (int)Win32Api.GetWindowLongPtr(hwnd, Win32Api.GWL_EXSTYLE);
        exStyle |= Win32Api.WS_EX_LAYERED | Win32Api.WS_EX_TRANSPARENT | Win32Api.WS_EX_NOACTIVATE | Win32Api.WS_EX_TOOLWINDOW | Win32Api.WS_EX_TOPMOST;
        Win32Api.SetWindowLongPtr(hwnd, Win32Api.GWL_EXSTYLE, (IntPtr)exStyle);

        // Exclude from screenshots
        Win32Api.SetWindowDisplayAffinity(hwnd, Win32Api.WDA_EXCLUDEFROMCAPTURE);

        Reposition();
    }

    public void Reposition()
    {
        var primary = Screen.PrimaryScreen?.Bounds ?? new System.Drawing.Rectangle(0, 0, 1920, 1080);
        Left = primary.Left + (primary.Width - ActualWidth) / 2.0;
        Top = primary.Bottom - ActualHeight - 64;
    }

    public void SetState(string status, string micName, bool isRecording)
    {
        StatusText.Text = status;
        MicText.Text = micName;
        StatusDot.Fill = isRecording ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 68, 68)) : new SolidColorBrush(System.Windows.Media.Color.FromRgb(56, 189, 248));
        Reposition();
    }

    public void UpdateLevel(float level)
    {
        float clamped = Math.Clamp(level, 0.05f, 1.0f);
        Bar1.Height = Math.Max(4, clamped * 12);
        Bar2.Height = Math.Max(6, clamped * 20);
        Bar3.Height = Math.Max(8, clamped * 30);
        Bar4.Height = Math.Max(6, clamped * 20);
        Bar5.Height = Math.Max(4, clamped * 12);
    }
}
