using System.Drawing;
using System.Windows;
using System.Windows.Threading;
using AIUsageTracker.Services;
using AIUsageTracker.Services.Providers;
using AIUsageTracker.Views;

namespace AIUsageTracker;

public partial class App : System.Windows.Application
{
    private System.Windows.Forms.NotifyIcon? _trayIcon;
    private MainWindow? _mainWindow;
    private readonly DispatcherTimer _tooltipTimer = new() { Interval = TimeSpan.FromSeconds(5) };

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var storage = new StorageService();
        var api = new ClaudeApiService();
        var usage = new UsageService(storage, api);
        var geminiProvider = new GeminiProvider();
        var geminiAccounts = new GeminiAccountService(storage, geminiProvider);

        _mainWindow = new MainWindow(usage, api, storage, geminiAccounts, geminiProvider);
        MainWindow = _mainWindow;

        Logger.Info($"App started (v{UpdateService.CurrentVersion})");

        SetupTray();
        _mainWindow.Show();
    }

    private void SetupTray()
    {
        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Visible = true,
            Text = "A.I. Usage Tracker",
            Icon = SystemIcons.Application
        };

        try
        {
            var p = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "icon.ico");
            if (System.IO.File.Exists(p)) _trayIcon.Icon = new Icon(p);
        }
        catch (Exception ex) { Logger.Warn("Tray icon load failed", ex); }

        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Show Window", null, (_, _) => ShowWin());
        menu.Items.Add("Refresh Now", null, (_, _) => { ShowWin(); _mainWindow?.TriggerRefresh(); });
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => Quit());

        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.DoubleClick += (_, _) => ShowWin();

        _tooltipTimer.Tick += (_, _) =>
        {
            if (_trayIcon != null && _mainWindow != null)
                _trayIcon.Text = _mainWindow.GetTrayTooltip();
        };
        _tooltipTimer.Start();
    }

    private void ShowWin()
    {
        if (_mainWindow == null) return;
        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }

    private void Quit()
    {
        _mainWindow?.RealClose();
        _trayIcon?.Dispose();
        _trayIcon = null;
        Shutdown();
    }

    public static void ShowBalloon(string title, string text)
    {
        if (Current is App app && app._trayIcon != null)
            app._trayIcon.ShowBalloonTip(3000, title, text, System.Windows.Forms.ToolTipIcon.Warning);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_trayIcon != null)
        {
            _trayIcon.Dispose();
            _trayIcon = null;
        }
        base.OnExit(e);
    }
}
