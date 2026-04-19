using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using AIUsageTracker.Services;
using AIUsageTracker.Services.Providers;
using AIUsageTracker.Views;

namespace AIUsageTracker;

public partial class App : System.Windows.Application
{
    private const string SingleInstanceMutexName = "Global\\AIUsageTracker_SingleInstance";

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hWnd);

    private const int SW_RESTORE = 9;

    private System.Threading.Mutex? _instanceMutex;
    private System.Windows.Forms.NotifyIcon? _trayIcon;
    private MainWindow? _mainWindow;
    private readonly DispatcherTimer _tooltipTimer = new() { Interval = TimeSpan.FromSeconds(5) };

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _instanceMutex = new System.Threading.Mutex(true, SingleInstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            ActivateExistingInstance();
            _instanceMutex.Dispose();
            _instanceMutex = null;
            Shutdown();
            return;
        }

        var storage = new StorageService();
        var api = new ClaudeApiService();
        var usage = new UsageService(storage, api);
        var geminiProvider = new GeminiProvider();
        var geminiAccounts = new GeminiAccountService(storage, geminiProvider);
        var anthropicProvider = new AnthropicApiProvider();
        var anthropicAccounts = new AnthropicApiAccountService(storage, anthropicProvider);
        var openAiProvider = new OpenAiApiProvider();
        var openAiAccounts = new OpenAiApiAccountService(storage, openAiProvider);
        var codex = new CodexCliService();

        _mainWindow = new MainWindow(usage, api, storage, geminiAccounts, geminiProvider,
                                     anthropicAccounts, openAiAccounts, codex);
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

    private static void ActivateExistingInstance()
    {
        try
        {
            var current = Process.GetCurrentProcess();
            var others = Process.GetProcessesByName(current.ProcessName)
                .Where(p => p.Id != current.Id);
            foreach (var p in others)
            {
                var hWnd = p.MainWindowHandle;
                if (hWnd == IntPtr.Zero) continue;
                if (IsIconic(hWnd)) ShowWindow(hWnd, SW_RESTORE);
                SetForegroundWindow(hWnd);
                return;
            }
        }
        catch (Exception ex) { Logger.Warn("ActivateExistingInstance failed", ex); }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_trayIcon != null)
        {
            _trayIcon.Dispose();
            _trayIcon = null;
        }
        if (_instanceMutex != null)
        {
            try { _instanceMutex.ReleaseMutex(); } catch { }
            _instanceMutex.Dispose();
            _instanceMutex = null;
        }
        base.OnExit(e);
    }
}
