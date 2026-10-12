using System.Windows;
using BetterVoice.App.UI;

namespace BetterVoice.App;

public partial class App : System.Windows.Application
{
    private AppController? _controller;
    private TrayIconManager? _trayManager;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _controller = new AppController();
        _controller.Start();

        _trayManager = new TrayIconManager(_controller);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayManager?.Dispose();
        _controller?.Dispose();
        base.OnExit(e);
    }
}
