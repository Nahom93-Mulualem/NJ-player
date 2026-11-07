using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using NJPlayer.Core.Interop;

namespace NJPlayer.App;

/// <summary>
/// Interaction logic for App.xaml with single-instance named-pipe IPC and --bench cold start measurement.
/// </summary>
public partial class App : Application
{
    private SingleInstanceManager? _singleInstanceManager;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var stopwatch = Stopwatch.StartNew();
        bool isBench = e.Args.Any(a => string.Equals(a, "--bench", StringComparison.OrdinalIgnoreCase));

        _singleInstanceManager = new SingleInstanceManager();
        if (!_singleInstanceManager.TryAcquire(e.Args))
        {
            // Another instance is already running; command-line arguments have been forwarded via IPC.
            // Shut down this second instance immediately.
            Shutdown();
            return;
        }

        var mainWindow = new MainWindow(isBench ? stopwatch : null);

        _singleInstanceManager.ArgumentsReceived += args =>
        {
            Dispatcher.InvokeAsync(() =>
            {
                mainWindow.HandleCommandLineArgs(args);
                mainWindow.Activate();
                if (mainWindow.WindowState == WindowState.Minimized)
                {
                    mainWindow.WindowState = WindowState.Normal;
                }
            });
        };

        mainWindow.Show();
        mainWindow.HandleCommandLineArgs(e.Args);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstanceManager?.Dispose();
        base.OnExit(e);
    }
}
