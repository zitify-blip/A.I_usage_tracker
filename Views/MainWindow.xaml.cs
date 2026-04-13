using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using ClaudeUsageTracker.Models;
using ClaudeUsageTracker.Services;
using Color = System.Windows.Media.Color;
using Brush = System.Windows.Media.Brush;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace ClaudeUsageTracker.Views;

public partial class MainWindow : Window
{
    private readonly UsageService _usage;
    private readonly ClaudeApiService _api;
    private readonly UpdateService _update = new();
    private readonly DispatcherTimer _pollTimer;
    private readonly DispatcherTimer _tickTimer;
    private bool _reallyClosing;
    private bool _notified;
    private UpdateInfo? _pendingUpdate;

    private const int SessionTotalMs = 5 * 60 * 60 * 1000;
    private const long WeekTotalMs = 7L * 24 * 60 * 60 * 1000;

    public MainWindow(UsageService usage, ClaudeApiService api)
    {
        InitializeComponent();
        _usage = usage;
        _api = api;

        _usage.StatusChanged += () => Dispatcher.Invoke(UpdateStatus);
        _usage.UsageUpdated += () => Dispatcher.Invoke(UpdateUI);

        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
        _pollTimer.Tick += async (_, _) => await Fetch();

        _tickTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _tickTimer.Tick += (_, _) => Tick();
        _tickTimer.Start();

        Loaded += async (_, _) => await StartUp();

        VersionLabel.Text = $"v{UpdateService.CurrentVersion}";
    }

    // ────────── Startup ──────────

    private async Task StartUp()
    {
        _usage.SetStatus("Loading claude.ai...", "loading");

        // Initialize hidden WebView2 (shares cookies with LoginWindow)
        await _api.InitializeAsync(BgWebView);

        // Try fetching immediately
        var result = await Fetch();

        // If needs login, open login window
        if (result == null)
            OpenLogin();

        // Check for updates in background
        _ = CheckForUpdateAsync();
    }

    private async Task CheckForUpdateAsync()
    {
        var info = await _update.CheckForUpdateAsync();
        if (info != null)
        {
            _pendingUpdate = info;
            UpdateBtn.Visibility = Visibility.Visible;
            App.ShowBalloon("업데이트 알림", $"새 버전 v{info.Version}이 있습니다!");
        }
    }

    // ────────── Fetch ──────────

    private async Task<bool?> Fetch()
    {
        var result = await _usage.FetchUsageAsync();
        if (result == true)
        {
            _pollTimer.Start();
            LastUpdateLabel.Text = $"Last update: {DateTime.Now:HH:mm:ss}";
            CheckNotify();
        }
        return result;
    }

    private void CheckNotify()
    {
        var l = _usage.Latest;
        if ((l.SessionPct >= 80 || l.WeekPct >= 80) && !_notified)
        {
            _notified = true;
            App.ShowBalloon("Claude Usage Alert", $"Session: {l.SessionPct:F0}% · Week: {l.WeekPct:F0}%");
        }
        else if (l.SessionPct < 80 && l.WeekPct < 80)
            _notified = false;
    }

    // ────────── Status ──────────

    private void UpdateStatus()
    {
        StatusLabel.Text = _usage.StatusText;
        StatusLabel.Foreground = _usage.StatusKind switch
        {
            "connected" => B("#4ade80"),
            "loading" => B("#facc15"),
            "error" => B("#f87171"),
            _ => B("#888888")
        };

        if (_usage.IsLoggedIn)
        {
            LoginBtn.Content = "Logout";
            LoginBtn.Background = B("#262626");
        }
        else
        {
            LoginBtn.Content = "Login";
            LoginBtn.Background = _usage.StatusKind == "error" ? B("#f87171") : B("#262626");
        }
    }

    // ────────── Full UI Update ──────────

    private void UpdateUI()
    {
        var l = _usage.Latest;

        SetRing(UsageRingFigure, UsageRingArc, UsageRingPath, l.SessionPct, UsageRingBrush, true);
        UsagePctText.Text = $"{l.SessionPct:F0}%";
        UsagePctText.Foreground = UsageColor(l.SessionPct);

        UpdateTimeRing(l);

        WeekAllPctText.Text = $"{l.WeekPct:F0}%";
        WeekAllPctText.Foreground = UsageColor(l.WeekPct);
        SetBar(WeekAllBar, l.WeekPct);
        SetMarker(WeekAllMarker, WeekAllMarkerLabel, WeekAllMarkerCanvas, l.WeekResetAt);
        WeekAllResetText.Text = FmtResetIn(l.WeekResetAt);

        SubModelTitle.Text = $"WEEKLY · {l.SubModelName.ToUpper()}";
        SubPctText.Text = $"{l.SubPct:F0}%";
        SubPctText.Foreground = UsageColor(l.SubPct);
        SetBar(SubBar, l.SubPct);
        SetMarker(SubMarker, SubMarkerLabel, SubMarkerCanvas, l.SubResetAt);
        SubResetText.Text = FmtResetIn(l.SubResetAt);

        RenderExtra(l.Extra);
        DrawChart();
    }

    // ────────── Tick (1s) ──────────

    private void Tick()
    {
        var l = _usage.Latest;
        if (l.SessionResetAt == null) return;
        UpdateTimeRing(l);
        SetMarker(WeekAllMarker, WeekAllMarkerLabel, WeekAllMarkerCanvas, l.WeekResetAt);
        SetMarker(SubMarker, SubMarkerLabel, SubMarkerCanvas, l.SubResetAt);
    }

    private void UpdateTimeRing(LatestUsage l)
    {
        if (l.SessionResetAt == null || !DateTimeOffset.TryParse(l.SessionResetAt, out var rst)) return;
        var rem = Math.Max(0, (rst - DateTimeOffset.Now).TotalMilliseconds);
        var pct = rem / SessionTotalMs * 100;
        SetRing(TimeRingFigure, TimeRingArc, TimeRingPath, pct, TimeRingBrush, false);
        TimeLeftText.Text = FmtRemain((long)rem);
        TimeLeftPctText.Text = $"{pct:F0}% of 5h left";
        SessionResetAtLabel.Text = $"Resets at {rst.ToLocalTime():ddd HH:mm}";
        TimeRingBrush.Color = pct > 30 ? C("#60a5fa") : pct > 10 ? C("#facc15") : C("#f87171");
    }

    // ────────── Ring ──────────

    private static void SetRing(PathFigure fig, ArcSegment arc, Path path, double pct,
        SolidColorBrush brush, bool isUsage)
    {
        pct = Math.Clamp(pct, 0, 100);
        if (pct < 0.5) { path.Visibility = Visibility.Collapsed; return; }
        path.Visibility = Visibility.Visible;

        var angle = Math.Min(pct / 100.0 * 360.0, 359.99);
        var rad = angle * Math.PI / 180.0;
        const double cx = 100, cy = 100, r = 86;

        fig.StartPoint = new Point(cx, cy - r);
        arc.Point = new Point(cx + r * Math.Sin(rad), cy - r * Math.Cos(rad));
        arc.Size = new Size(r, r);
        arc.IsLargeArc = angle > 180;

        if (isUsage)
            brush.Color = pct >= 90 ? C("#f87171") : pct >= 70 ? C("#facc15") : C("#4ade80");
    }

    // ────────── Bar / Marker ──────────

    private static void SetBar(Border bar, double pct)
    {
        if (bar.Parent is not Grid g || g.ActualWidth <= 0) return;
        bar.Width = g.ActualWidth * Math.Clamp(pct, 0, 100) / 100.0;
        bar.Background = UsageColor(pct);
    }

    private static void SetMarker(Grid marker, TextBlock label, Canvas canvas, string? iso)
    {
        if (string.IsNullOrEmpty(iso) || !DateTimeOffset.TryParse(iso, out var rst))
        { marker.Visibility = Visibility.Collapsed; return; }

        marker.Visibility = Visibility.Visible;
        var rem = Math.Max(0, (rst - DateTimeOffset.Now).TotalMilliseconds);
        var elapsed = Math.Max(0, WeekTotalMs - rem);
        var pct = Math.Min(100, (double)elapsed / WeekTotalMs * 100);
        var w = canvas.ActualWidth > 0 ? canvas.ActualWidth : 300;
        Canvas.SetLeft(marker, w * pct / 100.0);
        label.Text = $"{pct:F0}%";
    }

    // ────────── Extra Usage ──────────

    private void RenderExtra(ExtraUsage? ex)
    {
        if (ex == null || !ex.IsEnabled)
        {
            ExtraCard.Opacity = 0.5;
            ExtraUsedText.Text = "$0.00";
            ExtraLimitText.Text = "of $0.00";
            ExtraPctText.Text = "0%";
            ExtraDisabledText.Visibility = ex?.IsEnabled == false ? Visibility.Visible : Visibility.Collapsed;
            return;
        }
        ExtraCard.Opacity = 1;
        ExtraDisabledText.Visibility = Visibility.Collapsed;
        var used = ex.UsedCredits / 100.0;
        var limit = ex.MonthlyLimit / 100.0;
        var pct = Math.Round(ex.Utilization);
        ExtraUsedText.Text = $"${used:F2}";
        ExtraLimitText.Text = $"of ${limit:F2}";
        ExtraPctText.Text = $"{pct:F0}%";
        if (ExtraBar.Parent is Grid g && g.ActualWidth > 0)
            ExtraBar.Width = g.ActualWidth * Math.Min(100, pct) / 100.0;
    }

    // ────────── Delta Chart ──────────

    private void DrawChart()
    {
        var hist = _usage.GetHistory();
        DeltaChartCanvas.Children.Clear();

        if (hist.Count < 2)
        {
            DeltaEmptyText.Text = $"Collecting data... {hist.Count} snapshot(s) so far";
            DeltaEmptyText.Visibility = Visibility.Visible;
            return;
        }
        DeltaEmptyText.Visibility = Visibility.Collapsed;

        var recent = hist.TakeLast(61).ToList();
        var points = new List<(double delta, long ts)>();
        for (int i = 1; i < recent.Count; i++)
        {
            var diff = recent[i].FiveHourUtilization - recent[i - 1].FiveHourUtilization;
            points.Add((Math.Max(0, diff), recent[i].Timestamp));
        }
        if (points.Count < 2) return;

        var cw = DeltaChartCanvas.ActualWidth;
        var ch = DeltaChartCanvas.ActualHeight;
        if (cw <= 0 || ch <= 0) return;

        double left = 32, right = 8, top = 4, bottom = 18;
        var plotW = cw - left - right;
        var plotH = ch - top - bottom;

        var maxPct = Math.Max(5, points.Max(p => p.delta));
        maxPct = maxPct <= 5 ? 5 : maxPct <= 25 ? Math.Ceiling(maxPct / 5) * 5 : Math.Ceiling(maxPct / 10) * 10;

        // Grid lines
        var gridStep = maxPct <= 20 ? 5.0 : maxPct <= 50 ? 10.0 : 20.0;
        for (double y = 0; y <= maxPct; y += gridStep)
        {
            var py = top + plotH - (y / maxPct * plotH);
            DeltaChartCanvas.Children.Add(MkText($"{y:F0}%", 0, py - 6, 8, "#555"));
            DeltaChartCanvas.Children.Add(new Line
            {
                X1 = left, X2 = cw - right, Y1 = py, Y2 = py,
                Stroke = B("#1a1a1a"), StrokeThickness = 0.5
            });
        }

        // Build polyline points
        var linePoints = new PointCollection();
        var fillPoints = new PointCollection();
        var baseY = top + plotH;

        for (int i = 0; i < points.Count; i++)
        {
            var x = left + (plotW * i / (points.Count - 1));
            var y = top + plotH - (points[i].delta / maxPct * plotH);
            linePoints.Add(new Point(x, y));
            fillPoints.Add(new Point(x, y));
        }

        // Gradient fill under the line
        fillPoints.Add(new Point(left + plotW, baseY));
        fillPoints.Add(new Point(left, baseY));

        var fillPolygon = new Polygon
        {
            Points = fillPoints,
            Fill = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new(Color.FromArgb(60, 74, 222, 128), 0),   // #4ade80 with alpha
                    new(Color.FromArgb(5, 74, 222, 128), 1)
                }, 90)
        };
        DeltaChartCanvas.Children.Add(fillPolygon);

        // Main line
        var polyline = new Polyline
        {
            Points = linePoints,
            Stroke = B("#4ade80"),
            StrokeThickness = 2,
            StrokeLineJoin = PenLineJoin.Round
        };
        DeltaChartCanvas.Children.Add(polyline);

        // Dots at each data point
        for (int i = 0; i < linePoints.Count; i++)
        {
            var pt = linePoints[i];
            var val = points[i].delta;
            var dotColor = val >= 15 ? "#f87171" : val >= 8 ? "#facc15" : "#4ade80";
            var dot = new Ellipse
            {
                Width = 5, Height = 5,
                Fill = B(dotColor)
            };
            Canvas.SetLeft(dot, pt.X - 2.5);
            Canvas.SetTop(dot, pt.Y - 2.5);
            DeltaChartCanvas.Children.Add(dot);
        }

        // Reset lines (where usage dropped significantly = session reset)
        // Reset lines not needed for delta view

        // Time labels
        var t0 = DateTimeOffset.FromUnixTimeMilliseconds(points[0].ts).ToLocalTime();
        var t1 = DateTimeOffset.FromUnixTimeMilliseconds(points[^1].ts).ToLocalTime();
        DeltaChartCanvas.Children.Add(MkText(t0.ToString("HH:mm"), left, ch - 14, 8, "#666"));
        DeltaChartCanvas.Children.Add(MkText(t1.ToString("HH:mm"), cw - right - 28, ch - 14, 8, "#666"));

        // Latest value label
        var lastPt = linePoints[^1];
        var lastVal = points[^1].delta;
        DeltaChartCanvas.Children.Add(MkText($"+{lastVal:F1}%", lastPt.X + 4, lastPt.Y - 6, 9,
            lastVal >= 15 ? "#f87171" : lastVal >= 8 ? "#facc15" : "#4ade80"));
    }

    private void DeltaChartCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => DrawChart();

    // ────────── Login ──────────

    private async void OpenLogin()
    {
        var loginWin = new LoginWindow { Owner = this };
        loginWin.ShowDialog();

        // If LoginWindow already fetched usage data successfully, use it directly
        if (loginWin.LoginSuccess && !string.IsNullOrEmpty(loginWin.FetchResultJson))
        {
            if (_usage.ProcessRawFetchResult(loginWin.FetchResultJson))
            {
                LastUpdateLabel.Text = $"Last update: {DateTime.Now:HH:mm:ss}";
                CheckNotify();

                // Reload BgWebView in background for future polling
                _ = Task.Run(async () =>
                {
                    await Dispatcher.InvokeAsync(async () =>
                    {
                        await _api.ReloadAsync();
                    });
                });
                _pollTimer.Start();
                return;
            }
        }

        // Fallback: reload hidden WebView and retry fetch
        _usage.SetStatus("Reloading...", "loading");
        await _api.ReloadAsync();

        for (int i = 0; i < 3; i++)
        {
            var result = await Fetch();
            if (result == true) return;
            await Task.Delay(2000);
            if (i < 2) await _api.ReloadAsync();
        }
    }

    // ────────── Events ──────────

    private async void UpdateBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingUpdate == null) return;

        UpdateBtn.IsEnabled = false;

        // Find the TextBlock inside the button template
        void SetBtnText(string text)
        {
            StatusLabel.Text = text;
            StatusLabel.Foreground = B("#facc15");
        }

        SetBtnText($"v{_pendingUpdate.Version} 다운로드 중...");

        var success = await _update.DownloadAndInstallAsync(_pendingUpdate, pct =>
        {
            Dispatcher.Invoke(() => SetBtnText($"다운로드 중... {pct}%"));
        });

        if (success)
        {
            SetBtnText("설치 프로그램 실행됨. 앱을 종료합니다...");
            await Task.Delay(1500);
            _reallyClosing = true;
            System.Windows.Application.Current.Shutdown();
        }
        else
        {
            SetBtnText("다운로드 실패");
            UpdateBtn.IsEnabled = true;
        }
    }

    private void OpenClaudeBtn_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("https://claude.ai") { UseShellExecute = true }); }
        catch { }
    }

    private async void CheckUpdateBtn_Click(object sender, RoutedEventArgs e)
    {
        CheckUpdateBtn.IsEnabled = false;
        CheckUpdateBtn.Content = "🔄 확인 중...";

        var info = await _update.CheckForUpdateAsync();
        if (info != null)
        {
            _pendingUpdate = info;
            UpdateBtn.Visibility = Visibility.Visible;
            CheckUpdateBtn.Content = $"🔄 v{info.Version} 발견!";
        }
        else
        {
            CheckUpdateBtn.Content = "✓ 최신 버전";
            await Task.Delay(2000);
            CheckUpdateBtn.Content = "🔄 Update";
        }
        CheckUpdateBtn.IsEnabled = true;
    }

    private void TopMostBtn_Click(object sender, RoutedEventArgs e)
    {
        Topmost = !Topmost;
        TopMostBtn.Content = Topmost ? "📌 On" : "📌";
        TopMostBtn.Background = Topmost ? B("#4ade80") : B("#262626");
        TopMostBtn.Foreground = Topmost ? B("#000000") : B("#e8e8e8");
    }

    private async void RefreshBtn_Click(object sender, RoutedEventArgs e) => await Fetch();

    private async void LoginBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_usage.IsLoggedIn)
        {
            // Logout: clear WebView2 cookies and reset state
            _pollTimer.Stop();
            _usage.SetStatus("Logging out...", "loading");

            if (BgWebView.CoreWebView2 != null)
            {
                var cookieManager = BgWebView.CoreWebView2.CookieManager;
                cookieManager.DeleteAllCookies();
            }

            _usage.Logout();
            UpdateUI();

            // Reload BgWebView with cleared cookies
            await _api.ReloadAsync();

            // Open login window for new account
            OpenLogin();
        }
        else
        {
            OpenLogin();
        }
    }

    private void CreditLink_Click(object sender, MouseButtonEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("https://zitify.co.kr") { UseShellExecute = true }); }
        catch { }
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_reallyClosing) { e.Cancel = true; Hide(); }
    }

    public void RealClose()
    {
        _reallyClosing = true;
        _pollTimer.Stop();
        _tickTimer.Stop();
        Close();
    }

    public void TriggerRefresh() => Dispatcher.InvokeAsync(async () => await Fetch());

    public string GetTrayTooltip()
    {
        var l = _usage.Latest;
        return $"Claude Usage\nSession: {l.SessionPct:F0}%\nWeek: {l.WeekPct:F0}%";
    }

    // ────────── Helpers ──────────

    private static Color C(string hex) =>
        (Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);

    private static SolidColorBrush B(string hex) => new(C(hex));

    private static SolidColorBrush UsageColor(double pct) =>
        pct >= 90 ? B("#f87171") : pct >= 70 ? B("#facc15") : B("#4ade80");

    private static string FmtRemain(long ms)
    {
        if (ms <= 0) return "0:00";
        var t = TimeSpan.FromMilliseconds(ms);
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes:D2}m" : $"{t.Minutes}:{t.Seconds:D2}";
    }

    private static string FmtResetIn(string? iso)
    {
        if (string.IsNullOrEmpty(iso) || !DateTimeOffset.TryParse(iso, out var dt)) return "Resets in --";
        var r = dt - DateTimeOffset.Now;
        if (r.TotalMilliseconds <= 0) return "Reset imminent";
        if (r.TotalDays >= 1) return $"Resets in {(int)r.TotalDays}d {r.Hours}h";
        if (r.TotalHours >= 1) return $"Resets in {(int)r.TotalHours}h {r.Minutes}m";
        return $"Resets in {r.Minutes}m";
    }

    private static TextBlock MkText(string text, double x, double y, double size, string color)
    {
        var tb = new TextBlock { Text = text, FontSize = size, Foreground = B(color) };
        Canvas.SetLeft(tb, x);
        Canvas.SetTop(tb, y);
        return tb;
    }
}
