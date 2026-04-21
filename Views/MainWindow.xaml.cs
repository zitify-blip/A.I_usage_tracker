using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using AIUsageTracker.Models;
using AIUsageTracker.Services;
using AIUsageTracker.Services.Providers;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using IOPath = System.IO.Path;
using IOFile = System.IO.File;

namespace AIUsageTracker.Views;

public partial class MainWindow : Window
{
    private readonly UsageService _usage;
    private readonly ClaudeApiService _api;
    private readonly StorageService _storage;
    private readonly GeminiAccountService _geminiAccounts;
    private readonly GeminiProvider _geminiProvider;
    private readonly AnthropicApiAccountService _anthropicAccounts;
    private readonly OpenAiApiAccountService _openAiAccounts;
    private readonly CodexCliService _codex;
    private readonly GrokApiAccountService _grokAccounts;
    private readonly GrokCliService _grokCli;
    private readonly GeminiRelayService _geminiRelay;
    private readonly UpdateService _update = new();
    private bool _suppressGeminiSelection;
    private bool _suppressAnthropicSelection;
    private bool _suppressOpenAiSelection;
    private bool _suppressGrokSelection;
    private int _anthropicRangeDays = 7;
    private int _openAiRangeDays = 7;
    private int _codexRangeDays = 7;
    private int _grokCliRangeDays = 7;
    private readonly DispatcherTimer _pollTimer;
    private readonly DispatcherTimer _tickTimer;
    private readonly DispatcherTimer _updateCheckTimer;
    private readonly Action _onStatusChanged;
    private readonly Action _onUsageUpdated;
    private Task? _updateCheckTask;
    private bool _reallyClosing;
    private bool _notified;
    private UpdateInfo? _pendingUpdate;
    private DateTimeOffset? _firedSessionResetAt;
    private bool _sessionResetDialogOpen;

    private const int SessionTotalMs = 5 * 60 * 60 * 1000;
    private const long WeekTotalMs = 7L * 24 * 60 * 60 * 1000;

    public MainWindow(UsageService usage, ClaudeApiService api, StorageService storage,
                       GeminiAccountService geminiAccounts, GeminiProvider geminiProvider,
                       AnthropicApiAccountService anthropicAccounts,
                       OpenAiApiAccountService openAiAccounts,
                       CodexCliService codex,
                       GrokApiAccountService grokAccounts,
                       GrokCliService grokCli,
                       GeminiRelayService geminiRelay)
    {
        _usage = usage;
        _api = api;
        _storage = storage;
        _geminiAccounts = geminiAccounts;
        _geminiProvider = geminiProvider;
        _anthropicAccounts = anthropicAccounts;
        _openAiAccounts = openAiAccounts;
        _codex = codex;
        _grokAccounts = grokAccounts;
        _grokCli = grokCli;
        _geminiRelay = geminiRelay;

        InitializeComponent();

        _grokAccounts.AccountsChanged += () => Dispatcher.Invoke(RefreshGrokUi);
        _grokAccounts.SelectedAccountChanged += () => Dispatcher.Invoke(RefreshGrokUi);

        _geminiAccounts.AccountsChanged += () => Dispatcher.Invoke(RefreshGeminiUi);
        _geminiAccounts.SelectedAccountChanged += () => Dispatcher.Invoke(RefreshGeminiUi);

        _geminiRelay.StatusChanged += () => Dispatcher.Invoke(RefreshGeminiRelayUi);
        _geminiRelay.UsageRecorded += _ => Dispatcher.Invoke(() =>
        {
            RefreshGeminiStats();
            RefreshGeminiRelayUi();
        });

        _anthropicAccounts.AccountsChanged += () => Dispatcher.Invoke(RefreshAnthropicUi);
        _anthropicAccounts.SelectedAccountChanged += () => Dispatcher.Invoke(RefreshAnthropicUi);

        _openAiAccounts.AccountsChanged += () => Dispatcher.Invoke(RefreshOpenAiUi);
        _openAiAccounts.SelectedAccountChanged += () => Dispatcher.Invoke(RefreshOpenAiUi);

        _onStatusChanged = () => Dispatcher.Invoke(UpdateStatus);
        _onUsageUpdated = () => Dispatcher.Invoke(UpdateUI);
        _usage.StatusChanged += _onStatusChanged;
        _usage.UsageUpdated += _onUsageUpdated;

        _pollTimer = new DispatcherTimer { Interval = CurrentPollInterval() };
        _pollTimer.Tick += async (_, _) => await Fetch();

        _tickTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _tickTimer.Tick += (_, _) => Tick();
        _tickTimer.Start();

        _updateCheckTimer = new DispatcherTimer { Interval = TimeSpan.FromHours(24) };
        _updateCheckTimer.Tick += async (_, _) => await CheckForUpdateAsync();
        _updateCheckTimer.Start();

        Loaded += async (_, _) => await StartUp();
        SizeChanged += (_, _) => { if (MainTabs?.SelectedIndex == 0) RefreshGlobalUi(); };

        VersionLabel.Text = $"v{UpdateService.CurrentVersion}";
    }

    private TimeSpan CurrentPollInterval() =>
        TimeSpan.FromSeconds(_storage.Settings.ClampedPollIntervalSeconds());

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

        // Check for updates in background (tracked for cleanup)
        _updateCheckTask = CheckForUpdateAsync();
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
        if (!_storage.Settings.NotifyEnabled) return;
        var threshold = _storage.Settings.ClampedNotifyThreshold();
        var l = _usage.Latest;
        if ((l.SessionPct >= threshold || l.WeekPct >= threshold) && !_notified)
        {
            _notified = true;
            App.ShowBalloon("Claude CLI Usage Alert", $"Session: {l.SessionPct:F0}% · Week: {l.WeekPct:F0}%");
        }
        else if (l.SessionPct < threshold && l.WeekPct < threshold)
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

        SetBar(UsageBar, l.SessionPct);
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

        RenderDesign(l);
        RenderRoutine(l);
        RenderExtra(l.Extra);
        DrawChart();

        // Keep Global tab in sync
        if (MainTabs?.SelectedIndex == 0) RefreshGlobalUi();
    }

    // ────────── Tick (1s) ──────────

    private void Tick()
    {
        var l = _usage.Latest;
        if (l.SessionResetAt == null) return;
        UpdateTimeRing(l);
        SetMarker(WeekAllMarker, WeekAllMarkerLabel, WeekAllMarkerCanvas, l.WeekResetAt);
        SetMarker(SubMarker, SubMarkerLabel, SubMarkerCanvas, l.SubResetAt);
        if (l.HasDesign)
            SetMarker(DesignMarker, DesignMarkerLabel, DesignMarkerCanvas, l.DesignResetAt);

        CheckSessionResetTrigger(l);
    }

    private void CheckSessionResetTrigger(LatestUsage l)
    {
        if (_sessionResetDialogOpen) return;
        if (l.SessionResetAt == null || !DateTimeOffset.TryParse(l.SessionResetAt, out var rst)) return;

        var now = DateTimeOffset.Now;
        if (now < rst) return;
        if (_firedSessionResetAt == rst) return;
        if ((now - rst) > TimeSpan.FromMinutes(15)) { _firedSessionResetAt = rst; return; }

        _firedSessionResetAt = rst;
        _ = ShowSessionResetDialogAsync();
    }

    private async Task ShowSessionResetDialogAsync()
    {
        _sessionResetDialogOpen = true;
        try
        {
            App.ShowBalloon("Claude 세션 리셋", "5시간 윈도가 리셋되었습니다. 새 세션을 시작하시겠어요?");

            var dialog = new SessionResetDialog { Owner = this };
            var ok = dialog.ShowDialog();
            if (ok != true) return;

            StatusLabel.Text = "메시지 전송 중...";
            StatusLabel.Foreground = B("#facc15");

            var (success, error) = await _api.SendMessageAsync(dialog.Message);
            if (success)
            {
                StatusLabel.Text = "새 세션 시작됨";
                StatusLabel.Foreground = B("#4ade80");
            }
            else
            {
                StatusLabel.Text = $"전송 실패: {error}";
                StatusLabel.Foreground = B("#f87171");
            }
        }
        finally
        {
            _sessionResetDialogOpen = false;
        }
    }

    private void UpdateTimeRing(LatestUsage l)
    {
        if (l.SessionResetAt == null || !DateTimeOffset.TryParse(l.SessionResetAt, out var rst)) return;
        var rem = Math.Max(0, (rst - DateTimeOffset.Now).TotalMilliseconds);
        var elapsedPct = Math.Clamp((SessionTotalMs - rem) / SessionTotalMs * 100, 0, 100);
        var remPct = 100 - elapsedPct;

        UpdateTimeBar(elapsedPct, remPct);
        TimeLeftText.Text = FmtRemain((long)rem);
        TimeLeftPctText.Text = $" left · {elapsedPct:F0}% elapsed";
        SessionResetAtLabel.Text = $"Resets at {rst.ToLocalTime():ddd HH:mm}";
        TimeLeftText.Foreground = remPct > 30 ? B("#60a5fa") : remPct > 10 ? B("#facc15") : B("#f87171");
    }

    private void UpdateTimeBar(double elapsedPct, double remPct)
    {
        if (TimeBarRoot.ActualWidth <= 0) return;
        var width = TimeBarRoot.ActualWidth;
        var fill = width * Math.Clamp(elapsedPct, 0, 100) / 100.0;
        TimeBar.Width = fill;

        var color = remPct > 30 ? C("#60a5fa") : remPct > 10 ? C("#facc15") : C("#f87171");
        TimeBar.Background = new SolidColorBrush(color);
        if (TimePlayhead.Children.Count > 0 && TimePlayhead.Children[0] is Ellipse e)
            e.Fill = new SolidColorBrush(color);

        Canvas.SetLeft(TimePlayhead, fill);
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

    // ────────── Claude Design ──────────

    private void RenderDesign(LatestUsage l)
    {
        if (!l.HasDesign)
        {
            DesignCard.Opacity = 0.4;
            DesignPctText.Text = "--";
            DesignPctText.Foreground = B("#666");
            DesignBar.Width = 0;
            DesignMarker.Visibility = Visibility.Collapsed;
            DesignResetText.Text = "Not available";
            return;
        }
        DesignCard.Opacity = 1;
        DesignPctText.Text = $"{l.DesignPct:F0}%";
        DesignPctText.Foreground = UsageColor(l.DesignPct);
        SetBar(DesignBar, l.DesignPct);
        SetMarker(DesignMarker, DesignMarkerLabel, DesignMarkerCanvas, l.DesignResetAt);
        DesignResetText.Text = FmtResetIn(l.DesignResetAt);
    }

    // ────────── Daily Routine ──────────

    private void RenderRoutine(LatestUsage l)
    {
        if (!l.HasRoutine)
        {
            RoutineCard.Opacity = 0.4;
            RoutineUsedText.Text = "--";
            RoutineLimitText.Text = "/ --";
            RoutineResetText.Text = "Not available";
            return;
        }
        RoutineCard.Opacity = 1;
        RoutineUsedText.Text = l.RoutineUsed.ToString();
        RoutineLimitText.Text = $"/ {l.RoutineLimit}";

        var pct = l.RoutineLimit > 0 ? (double)l.RoutineUsed / l.RoutineLimit * 100 : 0;
        RoutineUsedText.Foreground = UsageColor(pct);

        RoutineResetText.Text = string.IsNullOrEmpty(l.RoutineResetAt)
            ? "Daily limit"
            : FmtResetIn(l.RoutineResetAt);
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
        var used = (ex.UsedCredits ?? 0) / 100.0;
        var limit = (ex.MonthlyLimit ?? 0) / 100.0;
        var pct = Math.Round(ex.Utilization ?? 0);
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

                _ = Dispatcher.InvokeAsync(async () => await _api.ReloadAsync());
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
        UpdateBtn.IsEnabled = false;

        void SetBtnText(string text)
        {
            StatusLabel.Text = text;
            StatusLabel.Foreground = B("#facc15");
        }

        SetBtnText("최신 버전 확인 중...");

        var fresh = await _update.CheckForUpdateAsync();
        if (fresh == null)
        {
            _pendingUpdate = null;
            UpdateBtn.Visibility = Visibility.Collapsed;
            SetBtnText("이미 최신 버전입니다");
            return;
        }

        _pendingUpdate = fresh;
        SetBtnText($"v{fresh.Version} 다운로드 중...");

        var success = await _update.DownloadAndInstallAsync(fresh, pct =>
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
        catch (Exception ex) { Logger.Warn("OpenClaudeBtn_Click failed", ex); }
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
        catch (Exception ex) { Logger.Warn("CreditLink_Click failed", ex); }
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
        _usage.StatusChanged -= _onStatusChanged;
        _usage.UsageUpdated -= _onUsageUpdated;
        Close();
    }

    public void TriggerRefresh() => Dispatcher.InvokeAsync(async () => await Fetch());

    public string GetTrayTooltip()
    {
        var l = _usage.Latest;
        return $"A.I. Usage Tracker\nClaude CLI · Session: {l.SessionPct:F0}% · Week: {l.WeekPct:F0}%";
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

    // ────────── Gemini Tab ──────────

    private void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source is not System.Windows.Controls.TabControl) return;
        switch (MainTabs?.SelectedIndex)
        {
            case 0: RefreshGlobalUi(); break;    // Global
            case 2: RefreshAnthropicUi(); break; // Claude API
            case 3: RefreshGeminiUi(); break;    // Gemini API
            case 4: RefreshOpenAiUi(); break;    // OpenAI API
            case 5: RefreshCodexUi(); break;     // OpenAI CLI
            case 6: RefreshGrokUi(); break;      // Grok API
            case 7: RefreshGrokCliUi(); break;   // Grok CLI
        }
    }

    private void GlobalRefreshBtn_Click(object sender, RoutedEventArgs e) => RefreshGlobalUi();

    private void RefreshGlobalUi()
    {
        var now = DateTimeOffset.Now;
        var today = now.Date;
        var yesterday = today.AddDays(-1);
        var monthStart = new DateTime(now.Year, now.Month, 1);
        var monthEnd = monthStart.AddMonths(1);
        var daysInMonth = DateTime.DaysInMonth(now.Year, now.Month);
        var dayOfMonth = now.Day;

        long TsMs(DateTime d) => new DateTimeOffset(d, now.Offset).ToUnixTimeMilliseconds();

        var all = _storage.GetGeminiUsageHistory();
        var accounts = _geminiAccounts.GetAccounts();

        // ─── Row 1: Hero tiles ───
        var todayCost = all.Where(r => r.Timestamp >= TsMs(today) && r.Timestamp < TsMs(today.AddDays(1))).Sum(r => r.CostUsd);
        var yesterdayCost = all.Where(r => r.Timestamp >= TsMs(yesterday) && r.Timestamp < TsMs(today)).Sum(r => r.CostUsd);
        var monthRecs = all.Where(r => r.Timestamp >= TsMs(monthStart) && r.Timestamp < TsMs(monthEnd)).ToList();
        var monthCost = monthRecs.Sum(r => r.CostUsd);

        HeroTodayCost.Text = $"${todayCost:F2}";
        if (yesterdayCost > 0 || todayCost > 0)
        {
            var delta = todayCost - yesterdayCost;
            var sign = delta >= 0 ? "+" : "−";
            HeroTodayDelta.Text = $"yesterday ${yesterdayCost:F2} · {sign}${Math.Abs(delta):F2}";
            HeroTodayDelta.Foreground = delta > 0 ? B("#f87171") : delta < 0 ? B("#4ade80") : B("#888");
        }
        else
        {
            HeroTodayDelta.Text = "no usage yet";
            HeroTodayDelta.Foreground = B("#888");
        }

        HeroMonthCost.Text = $"${monthCost:F2}";
        var totalMonthlyBudget = accounts.Sum(a => a.MonthlyBudgetUsd);
        var daysRemaining = daysInMonth - dayOfMonth + 1;

        if (totalMonthlyBudget > 0)
        {
            var pct = monthCost / totalMonthlyBudget;
            SetRatioBar(HeroMonthBarFill, pct, BudgetColor(pct));
            HeroMonthSub.Text = $"${monthCost:F2} / ${totalMonthlyBudget:F2} ({pct:P0}) · {daysRemaining}d left";
        }
        else
        {
            HeroMonthBarFill.Width = 0;
            HeroMonthSub.Text = $"no budget set · {daysRemaining}d left";
        }

        var avgPerDay = dayOfMonth > 0 ? monthCost / dayOfMonth : 0;
        var projected = avgPerDay * daysInMonth;
        HeroProjectedCost.Text = $"${projected:F2}";
        if (totalMonthlyBudget > 0)
        {
            var projPct = projected / totalMonthlyBudget;
            HeroProjectedCost.Foreground = BudgetColor(projPct);
            HeroProjectedSub.Text = $"~${avgPerDay:F2}/day · {projPct:P0} of budget";
        }
        else
        {
            HeroProjectedCost.Foreground = B("#e8e8e8");
            HeroProjectedSub.Text = avgPerDay > 0 ? $"based on ${avgPerDay:F2}/day avg" : "no usage yet";
        }

        // ─── Row 2: Claude quota (rings) ───
        var l = _usage.Latest;
        SetRing(GSessionRingFigure, GSessionRingArc, GSessionRingPath, l.SessionPct, GSessionRingBrush, true);
        ClaudeSessionText.Text = $"{l.SessionPct:F0}%";
        ClaudeSessionText.Foreground = UsageColor(l.SessionPct);
        ClaudeSessionReset.Text = FmtResetIn(l.SessionResetAt);

        SetRing(GWeekRingFigure, GWeekRingArc, GWeekRingPath, l.WeekPct, GWeekRingBrush, true);
        ClaudeWeekText.Text = $"{l.WeekPct:F0}%";
        ClaudeWeekText.Foreground = UsageColor(l.WeekPct);
        ClaudeWeekReset.Text = FmtResetIn(l.WeekResetAt);

        // ─── Row 2: Gemini budgets ───
        var dayStartMs = TsMs(today);
        var todayByAcc = all.Where(r => r.Timestamp >= dayStartMs)
            .GroupBy(r => r.AccountId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.CostUsd));
        var monthByAcc = monthRecs.GroupBy(r => r.AccountId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.CostUsd));

        double budgetBarMax = 240;
        var budgetsContainer = GeminiBudgetsList.Parent as FrameworkElement;
        if (budgetsContainer != null && budgetsContainer.ActualWidth > 40)
            budgetBarMax = Math.Max(120, budgetsContainer.ActualWidth - 20);

        var budgetRows = accounts.Select(a => new GeminiBudgetRow(
            a,
            todayByAcc.TryGetValue(a.Id, out var d) ? d : 0,
            monthByAcc.TryGetValue(a.Id, out var m) ? m : 0,
            budgetBarMax
        )).ToList();
        GeminiBudgetsList.ItemsSource = budgetRows;
        GeminiBudgetsEmpty.Visibility = budgetRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        // ─── Row 3: Trend (7-day) ───
        Update7DayTrend(all);

        // ─── Row 3: Top models ───
        var byModel = monthRecs.GroupBy(r => r.Model)
            .Select(g => new { Model = g.Key, Cost = g.Sum(x => x.CostUsd) })
            .Where(x => x.Cost > 0)
            .OrderByDescending(x => x.Cost)
            .Take(3)
            .ToList();
        var topMax = byModel.FirstOrDefault()?.Cost ?? 1;
        if (topMax <= 0) topMax = 1;
        var totalMonth = monthRecs.Sum(r => r.CostUsd);
        if (totalMonth <= 0) totalMonth = 1;

        double topBarMax = 140;
        var topContainer = TopModelsList.Parent as FrameworkElement;
        if (topContainer != null && topContainer.ActualWidth > 40)
            topBarMax = Math.Max(80, topContainer.ActualWidth - 90);

        var topRows = byModel.Select(x => new TopModelRow(
            x.Model, x.Cost, x.Cost / topMax * topBarMax, x.Cost / totalMonth)).ToList();
        TopModelsList.ItemsSource = topRows;
        TopModelsEmpty.Visibility = topRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void SetRatioBar(Border bar, double ratio, SolidColorBrush color)
    {
        if (bar.Parent is not Grid g || g.ActualWidth <= 0)
        {
            bar.Width = 0;
            bar.Background = color;
            return;
        }
        bar.Width = Math.Max(0, Math.Min(1.0, ratio)) * g.ActualWidth;
        bar.Background = color;
    }

    private static SolidColorBrush BudgetColor(double pct)
    {
        if (pct >= 1.0) return B("#f87171");
        if (pct >= 0.8) return B("#fb923c");
        if (pct >= 0.6) return B("#facc15");
        return B("#4ade80");
    }

    private static string ModelFamily(string? model)
    {
        var m = (model ?? "").ToLowerInvariant();
        if (m.Contains("flash")) return "Flash";
        if (m.Contains("pro")) return "Pro";
        return "Other";
    }

    private static readonly Dictionary<string, string> FamilyColors = new()
    {
        ["Flash"] = "#4F7CE8",
        ["Pro"] = "#9B72CB",
        ["Other"] = "#64748b"
    };

    private void Update7DayTrend(IReadOnlyList<GeminiUsageRecord> all)
    {
        TrendCanvas.Children.Clear();
        TrendLegend.Children.Clear();

        var today = DateTimeOffset.Now.Date;
        var days = Enumerable.Range(0, 7).Select(i => today.AddDays(-6 + i)).ToList();

        var grid = days.ToDictionary(
            d => d,
            _ => new Dictionary<string, double> { ["Flash"] = 0, ["Pro"] = 0, ["Other"] = 0 });

        foreach (var r in all)
        {
            var dt = DateTimeOffset.FromUnixTimeMilliseconds(r.Timestamp).ToLocalTime().Date;
            if (!grid.ContainsKey(dt)) continue;
            grid[dt][ModelFamily(r.Model)] += r.CostUsd;
        }

        var dayLabels = new[] { TrendDay0, TrendDay1, TrendDay2, TrendDay3, TrendDay4, TrendDay5, TrendDay6 };
        for (int i = 0; i < 7; i++)
            dayLabels[i].Text = days[i].ToString("M/d");

        var dayTotals = days.Select(d => grid[d].Values.Sum()).ToList();
        var trendTotal = dayTotals.Sum();
        TrendTotalLabel.Text = $"7d · ${trendTotal:F2}";

        foreach (var fam in new[] { "Flash", "Pro", "Other" })
        {
            if (grid.Values.Any(x => x[fam] > 0))
                AddLegendItem(fam, FamilyColors[fam]);
        }

        var canvasWidth = TrendCanvas.ActualWidth > 0 ? TrendCanvas.ActualWidth : 480;
        var canvasHeight = TrendCanvas.ActualHeight > 0 ? TrendCanvas.ActualHeight : 160;
        var maxTotal = dayTotals.Count > 0 ? dayTotals.Max() : 0;
        if (maxTotal <= 0) maxTotal = 1;
        var slotW = canvasWidth / 7.0;
        var barW = Math.Max(6, slotW * 0.6);
        var gap = slotW - barW;
        var drawableH = canvasHeight - 16;

        for (int i = 0; i < 7; i++)
        {
            var total = dayTotals[i];
            var stackedBottom = canvasHeight;
            foreach (var fam in new[] { "Other", "Pro", "Flash" })
            {
                var cost = grid[days[i]][fam];
                if (cost <= 0) continue;
                var h = cost / maxTotal * drawableH;
                var rect = new System.Windows.Shapes.Rectangle
                {
                    Width = barW,
                    Height = h,
                    Fill = B(FamilyColors[fam]),
                    RadiusX = 2,
                    RadiusY = 2
                };
                Canvas.SetLeft(rect, i * slotW + gap / 2);
                Canvas.SetTop(rect, stackedBottom - h);
                TrendCanvas.Children.Add(rect);
                stackedBottom -= h;
            }
            if (total > 0)
            {
                var lbl = new TextBlock
                {
                    Text = total >= 1 ? $"${total:F2}" : $"${total:F3}",
                    FontSize = 9,
                    Foreground = B("#aaa")
                };
                Canvas.SetLeft(lbl, i * slotW + gap / 2 - 2);
                Canvas.SetTop(lbl, Math.Max(0, canvasHeight - total / maxTotal * drawableH - 13));
                TrendCanvas.Children.Add(lbl);
            }
        }
    }

    private void AddLegendItem(string label, string color)
    {
        var sp = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 0, 12, 0) };
        sp.Children.Add(new Border
        {
            Width = 8,
            Height = 8,
            CornerRadius = new CornerRadius(2),
            Background = B(color),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0)
        });
        sp.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 10,
            Foreground = B("#aaa"),
            VerticalAlignment = VerticalAlignment.Center
        });
        TrendLegend.Children.Add(sp);
    }

    private void RefreshGeminiUi()
    {
        var accounts = _geminiAccounts.GetAccounts();
        if (accounts.Count == 0)
        {
            GeminiEmptyState.Visibility = Visibility.Visible;
            GeminiDashboard.Visibility = Visibility.Collapsed;
            GeminiAccountCombo.ItemsSource = null;
            return;
        }

        GeminiEmptyState.Visibility = Visibility.Collapsed;
        GeminiDashboard.Visibility = Visibility.Visible;

        _suppressGeminiSelection = true;
        GeminiAccountCombo.ItemsSource = accounts.Select(a => new GeminiAccountDisplay(a)).ToList();
        var selected = _geminiAccounts.GetSelected();
        if (selected != null)
            GeminiAccountCombo.SelectedIndex = accounts.ToList().FindIndex(a => a.Id == selected.Id);
        _suppressGeminiSelection = false;

        if (selected != null)
        {
            GeminiActiveAlias.Text = selected.Alias;
            GeminiActiveKeyPreview.Text = selected.KeyPreview;
        }

        RefreshGeminiStats();
        RefreshGeminiRelayUi();
    }

    private void RefreshGeminiRelayUi()
    {
        if (GeminiRelayPortBox == null) return; // not yet loaded

        // Sync port box from settings (one-shot on first load)
        if (string.IsNullOrWhiteSpace(GeminiRelayPortBox.Text) ||
            GeminiRelayPortBox.Text == "47821" && _storage.Settings.GeminiRelayPort != 47821)
        {
            GeminiRelayPortBox.Text = _storage.Settings.ClampedGeminiRelayPort().ToString();
        }

        GeminiRelayAutoStartCheck.IsChecked = _storage.Settings.GeminiRelayAutoStart;

        if (_geminiRelay.IsRunning)
        {
            GeminiRelayStatusText.Text = $"● Running on 127.0.0.1:{_geminiRelay.Port}";
            GeminiRelayStatusText.Foreground = B("#4ade80");
            GeminiRelayStartBtn.Content = "⏹ Stop";
            GeminiRelayStartBtn.Background = B("#3a1a1a");
            GeminiRelayStartBtn.BorderBrush = B("#f87171");
            GeminiRelayPortBox.IsEnabled = false;

            var stats = $"served: {_geminiRelay.RequestsServed}";
            if (_geminiRelay.StartedAt.HasValue)
            {
                var up = DateTime.Now - _geminiRelay.StartedAt.Value;
                stats += $" · up {(up.TotalHours >= 1 ? $"{up.TotalHours:F1}h" : $"{up.TotalMinutes:F0}m")}";
            }
            GeminiRelayStatsText.Text = stats;
        }
        else
        {
            var err = _geminiRelay.LastError;
            GeminiRelayStatusText.Text = string.IsNullOrEmpty(err) ? "● Stopped" : $"● Error: {err}";
            GeminiRelayStatusText.Foreground = B(string.IsNullOrEmpty(err) ? "#888" : "#f87171");
            GeminiRelayStartBtn.Content = "▶ Start";
            GeminiRelayStartBtn.Background = B("#1a3a1a");
            GeminiRelayStartBtn.BorderBrush = B("#4ade80");
            GeminiRelayPortBox.IsEnabled = true;
            GeminiRelayStatsText.Text = "";
        }
    }

    private void GeminiRelayStartBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_geminiRelay.IsRunning)
        {
            _geminiRelay.Stop();
            return;
        }

        if (!int.TryParse(GeminiRelayPortBox.Text?.Trim(), out var port) || port < 1024 || port > 65535)
        {
            GeminiRelayStatusText.Text = "Invalid port (1024-65535)";
            GeminiRelayStatusText.Foreground = B("#f87171");
            return;
        }

        _storage.Settings.GeminiRelayPort = port;
        _storage.SaveSettings(_storage.Settings);

        if (!_geminiRelay.Start(port, out var err))
        {
            GeminiRelayStatusText.Text = $"● Start failed: {err}";
            GeminiRelayStatusText.Foreground = B("#f87171");
        }
    }

    private void GeminiRelayAutoStart_Changed(object sender, RoutedEventArgs e)
    {
        if (GeminiRelayAutoStartCheck == null) return;
        _storage.Settings.GeminiRelayAutoStart = GeminiRelayAutoStartCheck.IsChecked == true;
        _storage.SaveSettings(_storage.Settings);
    }

    private void GeminiRelayCopyBtn_Click(object sender, RoutedEventArgs e)
    {
        var port = _geminiRelay.IsRunning ? _geminiRelay.Port
                                          : _storage.Settings.ClampedGeminiRelayPort();
        var selected = _geminiAccounts.GetSelected();
        var alias = selected?.Alias ?? "default";

        var win = new GeminiRelayHelpWindow(port, alias) { Owner = this };
        win.ShowDialog();
    }

    private void RefreshGeminiStats()
    {
        var selected = _geminiAccounts.GetSelected();
        if (selected == null) return;

        var history = _storage.GetGeminiUsageHistory(selected.Id);
        var todayStart = DateTimeOffset.Now.Date;
        var todayMs = new DateTimeOffset(todayStart).ToUnixTimeMilliseconds();
        var today = history.Where(r => r.Timestamp >= todayMs).ToList();
        var last24h = DateTimeOffset.UtcNow.AddHours(-24).ToUnixTimeMilliseconds();
        var last24hRecords = history.Where(r => r.Timestamp >= last24h).ToList();

        var totalTokens = today.Sum(r => r.InputTokens + r.OutputTokens);
        var totalCost = today.Sum(r => r.CostUsd);

        GeminiTodayTokens.Text = totalTokens.ToString("N0");
        GeminiTodayCost.Text = $"${totalCost:F4}";
        GeminiRequestCount.Text = last24hRecords.Count.ToString();

        if (selected.LastUsedAtMs.HasValue)
        {
            var last = DateTimeOffset.FromUnixTimeMilliseconds(selected.LastUsedAtMs.Value).ToLocalTime();
            GeminiLastCallText.Text = $"last: {last:HH:mm:ss}";
        }
        else
        {
            GeminiLastCallText.Text = "no calls yet";
        }

        // Recent history (newest first, top 50)
        var recent = history.OrderByDescending(r => r.Timestamp).Take(50)
            .Select(r => new GeminiHistoryItem(r)).ToList();
        GeminiHistoryList.ItemsSource = recent;
        GeminiHistoryEmpty.Visibility = recent.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        // Account comparison (today cost per account)
        RefreshGeminiCompareList(todayMs);
    }

    private void RefreshGeminiCompareList(long todayMs)
    {
        var all = _storage.GetGeminiUsageHistory();
        var todayByAcc = all.Where(r => r.Timestamp >= todayMs)
            .GroupBy(r => r.AccountId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.CostUsd));

        var accounts = _geminiAccounts.GetAccounts();
        var rows = accounts.Select(a => new GeminiCompareRow(
            a.Alias,
            todayByAcc.TryGetValue(a.Id, out var c) ? c : 0
        )).ToList();

        var max = rows.Count > 0 ? rows.Max(r => r.Cost) : 0;
        if (max <= 0) max = 1;
        foreach (var r in rows) r.BarWidth = Math.Max(1, r.Cost / max * 320);
        GeminiCompareList.ItemsSource = rows;
    }

    private void CheckGeminiBudget(GeminiAccount account)
    {
        if (account.DailyBudgetUsd <= 0 && account.MonthlyBudgetUsd <= 0) return;

        var history = _storage.GetGeminiUsageHistory(account.Id);
        var now = DateTimeOffset.Now;

        var dayStart = new DateTimeOffset(now.Date, now.Offset).ToUnixTimeMilliseconds();
        var monthStart = new DateTimeOffset(new DateTime(now.Year, now.Month, 1), now.Offset).ToUnixTimeMilliseconds();

        var dailyUsed = history.Where(r => r.Timestamp >= dayStart).Sum(r => r.CostUsd);
        var monthlyUsed = history.Where(r => r.Timestamp >= monthStart).Sum(r => r.CostUsd);

        var threshold = Math.Clamp(account.AlertThresholdPct, 1, 100) / 100.0;

        // Daily
        if (account.DailyBudgetUsd > 0)
        {
            var dayKey = now.Date.ToString("yyyy-MM-dd");
            var pct = dailyUsed / account.DailyBudgetUsd;

            if (pct >= 1.0 && account.LastAlertedMaxKey != $"D:{dayKey}")
            {
                account.LastAlertedMaxKey = $"D:{dayKey}";
                _storage.Save();
                App.ShowBalloon($"Gemini 일간 예산 초과 · {account.Alias}",
                    $"${dailyUsed:F4} / ${account.DailyBudgetUsd:F2} ({pct:P0})");
            }
            else if (pct >= threshold && pct < 1.0 && account.LastAlertedWarnKey != $"D:{dayKey}")
            {
                account.LastAlertedWarnKey = $"D:{dayKey}";
                _storage.Save();
                App.ShowBalloon($"Gemini 일간 예산 경고 · {account.Alias}",
                    $"${dailyUsed:F4} / ${account.DailyBudgetUsd:F2} ({pct:P0})");
            }
        }

        // Monthly
        if (account.MonthlyBudgetUsd > 0)
        {
            var monthKey = now.ToString("yyyy-MM");
            var pct = monthlyUsed / account.MonthlyBudgetUsd;

            if (pct >= 1.0 && account.LastAlertedMaxKey != $"M:{monthKey}")
            {
                account.LastAlertedMaxKey = $"M:{monthKey}";
                _storage.Save();
                App.ShowBalloon($"Gemini 월간 예산 초과 · {account.Alias}",
                    $"${monthlyUsed:F2} / ${account.MonthlyBudgetUsd:F2} ({pct:P0})");
            }
            else if (pct >= threshold && pct < 1.0 && account.LastAlertedWarnKey != $"M:{monthKey}")
            {
                account.LastAlertedWarnKey = $"M:{monthKey}";
                _storage.Save();
                App.ShowBalloon($"Gemini 월간 예산 경고 · {account.Alias}",
                    $"${monthlyUsed:F2} / ${account.MonthlyBudgetUsd:F2} ({pct:P0})");
            }
        }
    }

    private void GeminiAccountCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressGeminiSelection) return;
        if (GeminiAccountCombo.SelectedItem is GeminiAccountDisplay d)
            _geminiAccounts.SelectAccount(d.Account.Id);
    }

    private async void GeminiAddAccountBtn_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new GeminiAddAccountDialog { Owner = this };
        if (dlg.ShowDialog() != true) return;

        GeminiStatusLabel.Text = "키 검증 중...";
        GeminiStatusLabel.Foreground = B("#facc15");

        var (ok, err, acc) = await _geminiAccounts.AddAccountAsync(dlg.Alias, dlg.ApiKey);
        if (!ok)
        {
            GeminiStatusLabel.Text = $"실패: {err}";
            GeminiStatusLabel.Foreground = B("#f87171");
            System.Windows.MessageBox.Show(this, $"계정 추가 실패: {err}",
                "Gemini", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        GeminiStatusLabel.Text = $"'{acc?.Alias}' 추가됨";
        GeminiStatusLabel.Foreground = B("#4ade80");
    }

    private void GeminiManageBtn_Click(object sender, RoutedEventArgs e)
    {
        var win = new GeminiAccountManagerWindow(_geminiAccounts) { Owner = this };
        win.ShowDialog();
        RefreshGeminiUi();
    }

    private void GeminiPricingBtn_Click(object sender, RoutedEventArgs e)
    {
        var win = new GeminiPricingEditorWindow(_storage) { Owner = this };
        win.ShowDialog();
        RefreshGeminiStats();
    }

    private async void GeminiTestBtn_Click(object sender, RoutedEventArgs e)
    {
        var selected = _geminiAccounts.GetSelected();
        if (selected == null) return;

        GeminiTestBtn.IsEnabled = false;
        GeminiStatusLabel.Text = "연결 테스트 중...";
        GeminiStatusLabel.Foreground = B("#facc15");

        var key = _geminiAccounts.GetApiKey(selected.Id);
        if (string.IsNullOrEmpty(key))
        {
            GeminiStatusLabel.Text = "키 복호화 실패";
            GeminiStatusLabel.Foreground = B("#f87171");
            GeminiTestBtn.IsEnabled = true;
            return;
        }

        var (ok, err, count) = await _geminiProvider.ValidateKeyAsync(key);
        if (ok)
        {
            GeminiStatusLabel.Text = $"연결 성공 · 사용 가능 모델 {count}개";
            GeminiStatusLabel.Foreground = B("#4ade80");

            selected.LastUsedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _storage.Save();
            RefreshGeminiStats();
        }
        else
        {
            GeminiStatusLabel.Text = $"연결 실패: {err}";
            GeminiStatusLabel.Foreground = B("#f87171");
        }
        GeminiTestBtn.IsEnabled = true;
    }

    private void GeminiRefreshBtn_Click(object sender, RoutedEventArgs e) => RefreshGeminiStats();

    private void GeminiExportBtn_Click(object sender, RoutedEventArgs e)
    {
        var selected = _geminiAccounts.GetSelected();
        var allAccounts = _geminiAccounts.GetAccounts();
        if (allAccounts.Count == 0)
        {
            GeminiStatusLabel.Text = "내보낼 계정이 없습니다";
            GeminiStatusLabel.Foreground = B("#f87171");
            return;
        }

        var defaultName = selected != null
            ? $"gemini-usage-{selected.Alias}-{DateTime.Now:yyyyMMdd-HHmmss}"
            : $"gemini-usage-all-{DateTime.Now:yyyyMMdd-HHmmss}";

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Gemini 사용 기록 내보내기",
            FileName = defaultName,
            Filter = "CSV (*.csv)|*.csv|JSON (*.json)|*.json",
            DefaultExt = ".csv"
        };
        if (dlg.ShowDialog(this) != true) return;

        var records = selected != null
            ? _storage.GetGeminiUsageHistory(selected.Id)
            : _storage.GetGeminiUsageHistory();

        var aliasMap = allAccounts.ToDictionary(a => a.Id, a => a.Alias);

        try
        {
            if (dlg.FileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                var payload = records.Select(r => new
                {
                    timestamp = DateTimeOffset.FromUnixTimeMilliseconds(r.Timestamp).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                    timestampMs = r.Timestamp,
                    accountId = r.AccountId,
                    accountAlias = aliasMap.GetValueOrDefault(r.AccountId, "(deleted)"),
                    model = r.Model,
                    inputTokens = r.InputTokens,
                    outputTokens = r.OutputTokens,
                    cacheTokens = r.CacheTokens,
                    thinkingTokens = r.ThinkingTokens,
                    toolTokens = r.ToolTokens,
                    costUsd = r.CostUsd,
                    latencyMs = r.LatencyMs
                });
                var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
                IOFile.WriteAllText(dlg.FileName, json, Encoding.UTF8);
            }
            else
            {
                var sb = new StringBuilder();
                sb.AppendLine("timestamp,account_alias,account_id,model,input_tokens,output_tokens,cache_tokens,thinking_tokens,tool_tokens,cost_usd,latency_ms");
                foreach (var r in records)
                {
                    var t = DateTimeOffset.FromUnixTimeMilliseconds(r.Timestamp).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
                    var alias = CsvEscape(aliasMap.GetValueOrDefault(r.AccountId, "(deleted)"));
                    sb.Append(t).Append(',')
                      .Append(alias).Append(',')
                      .Append(r.AccountId).Append(',')
                      .Append(CsvEscape(r.Model)).Append(',')
                      .Append(r.InputTokens).Append(',')
                      .Append(r.OutputTokens).Append(',')
                      .Append(r.CacheTokens).Append(',')
                      .Append(r.ThinkingTokens).Append(',')
                      .Append(r.ToolTokens).Append(',')
                      .Append(r.CostUsd.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                      .Append(r.LatencyMs)
                      .AppendLine();
                }
                IOFile.WriteAllText(dlg.FileName, sb.ToString(), new UTF8Encoding(true));
            }

            GeminiStatusLabel.Text = $"내보냄 · {records.Count}건 → {IOPath.GetFileName(dlg.FileName)}";
            GeminiStatusLabel.Foreground = B("#4ade80");
        }
        catch (Exception ex)
        {
            GeminiStatusLabel.Text = $"내보내기 실패: {ex.Message}";
            GeminiStatusLabel.Foreground = B("#f87171");
        }
    }

    private static string CsvEscape(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        if (s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r'))
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
    }

    // ────────── Claude API Tab (v2.6.0) ──────────

    private void RefreshAnthropicUi()
    {
        if (AnthropicAccountCombo == null) return;
        var accounts = _anthropicAccounts.GetAccounts();

        if (accounts.Count == 0)
        {
            AnthropicEmptyState.Visibility = Visibility.Visible;
            AnthropicDashboard.Visibility = Visibility.Collapsed;
            AnthropicAccountCombo.ItemsSource = null;
            return;
        }

        AnthropicEmptyState.Visibility = Visibility.Collapsed;
        AnthropicDashboard.Visibility = Visibility.Visible;

        _suppressAnthropicSelection = true;
        AnthropicAccountCombo.ItemsSource = accounts.Select(a => new AnthropicAccountDisplay(a)).ToList();
        var selected = _anthropicAccounts.GetSelected();
        if (selected != null)
        {
            for (int i = 0; i < AnthropicAccountCombo.Items.Count; i++)
            {
                if (AnthropicAccountCombo.Items[i] is AnthropicAccountDisplay d && d.Account.Id == selected.Id)
                {
                    AnthropicAccountCombo.SelectedIndex = i;
                    break;
                }
            }
            AnthropicActiveAlias.Text = selected.Alias;
            AnthropicActiveKeyPreview.Text = selected.KeyPreview;
            AnthropicActiveOrg.Text = string.IsNullOrEmpty(selected.OrganizationId) ? "" : $"org: {selected.OrganizationId}";
        }
        _suppressAnthropicSelection = false;

        RenderAnthropicUsage();
    }

    private void RenderAnthropicUsage()
    {
        var selected = _anthropicAccounts.GetSelected();
        if (selected == null) return;

        var history = _storage.GetAnthropicApiUsageHistory(selected.Id);
        var cutoff = DateTimeOffset.UtcNow.AddDays(-_anthropicRangeDays).ToUnixTimeMilliseconds();
        var recent = history.Where(r => r.Timestamp >= cutoff).ToList();

        var grouped = recent
            .GroupBy(r => r.Model)
            .Select(g => new AnthropicModelRow(
                g.Key,
                g.Sum(x => x.InputTokens),
                g.Sum(x => x.OutputTokens),
                g.Sum(x => x.CacheWriteTokens),
                g.Sum(x => x.CacheReadTokens),
                g.Sum(x => x.CostUsd)))
            .OrderByDescending(r => r.Cost)
            .ToList();

        AnthropicModelGrid.ItemsSource = grouped;
        AnthropicTotalCost.Text = $"${grouped.Sum(g => g.Cost):F4}";
        AnthropicTotalInput.Text = grouped.Sum(g => g.Input).ToString("N0");
        AnthropicTotalOutput.Text = grouped.Sum(g => g.Output).ToString("N0");
        AnthropicTotalCache.Text = (grouped.Sum(g => g.CacheWrite) + grouped.Sum(g => g.CacheRead)).ToString("N0");
    }

    private void AnthropicAccountCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressAnthropicSelection) return;
        if (AnthropicAccountCombo.SelectedItem is AnthropicAccountDisplay d)
            _anthropicAccounts.SelectAccount(d.Account.Id);
    }

    private void AnthropicRangeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AnthropicRangeCombo?.SelectedItem is ComboBoxItem item &&
            int.TryParse(item.Tag?.ToString(), out var days))
        {
            _anthropicRangeDays = days;
            RenderAnthropicUsage();
        }
    }

    private async void AnthropicAddAccountBtn_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new AnthropicAddAccountDialog { Owner = this };
        if (dlg.ShowDialog() != true) return;
        AnthropicStatusLabel.Text = "검증 중...";
        var (ok, err, _) = await _anthropicAccounts.AddAccountAsync(dlg.Alias, dlg.ApiKey);
        AnthropicStatusLabel.Text = ok ? "추가 완료" : $"실패: {err}";
        if (ok) await FetchAnthropicUsageAsync();
    }

    private async void AnthropicRefreshBtn_Click(object sender, RoutedEventArgs e)
    {
        await FetchAnthropicUsageAsync();
    }

    private async Task FetchAnthropicUsageAsync()
    {
        var selected = _anthropicAccounts.GetSelected();
        if (selected == null) return;
        AnthropicStatusLabel.Text = "조회 중...";
        AnthropicRefreshBtn.IsEnabled = false;
        try
        {
            var end = DateTimeOffset.UtcNow;
            var start = end.AddDays(-_anthropicRangeDays);
            var result = await _anthropicAccounts.FetchUsageAsync(selected.Id, start, end);
            if (!result.Ok)
            {
                AnthropicStatusLabel.Text = $"실패: {result.Error}";
                return;
            }
            AnthropicStatusLabel.Text = $"갱신 {DateTime.Now:HH:mm:ss} · {result.Buckets.Count} models";
            RenderAnthropicUsage();
        }
        finally
        {
            AnthropicRefreshBtn.IsEnabled = true;
        }
    }

    private void AnthropicRemoveBtn_Click(object sender, RoutedEventArgs e)
    {
        var selected = _anthropicAccounts.GetSelected();
        if (selected == null) return;
        var r = System.Windows.MessageBox.Show(this,
            $"'{selected.Alias}' 계정을 제거하시겠습니까?\n관련 사용량 이력도 함께 삭제됩니다.",
            "Claude API", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (r != MessageBoxResult.Yes) return;
        _anthropicAccounts.RemoveAccount(selected.Id);
    }

    // ──────────────── OpenAI API tab ────────────────

    private void RefreshOpenAiUi()
    {
        if (OpenAiAccountCombo == null) return;
        var accounts = _openAiAccounts.GetAccounts();

        if (accounts.Count == 0)
        {
            OpenAiEmptyState.Visibility = Visibility.Visible;
            OpenAiDashboard.Visibility = Visibility.Collapsed;
            OpenAiAccountCombo.ItemsSource = null;
            return;
        }

        OpenAiEmptyState.Visibility = Visibility.Collapsed;
        OpenAiDashboard.Visibility = Visibility.Visible;

        _suppressOpenAiSelection = true;
        OpenAiAccountCombo.ItemsSource = accounts.Select(a => new OpenAiAccountDisplay(a)).ToList();
        var selected = _openAiAccounts.GetSelected();
        if (selected != null)
        {
            for (int i = 0; i < OpenAiAccountCombo.Items.Count; i++)
            {
                if (OpenAiAccountCombo.Items[i] is OpenAiAccountDisplay d && d.Account.Id == selected.Id)
                {
                    OpenAiAccountCombo.SelectedIndex = i;
                    break;
                }
            }
            OpenAiActiveAlias.Text = selected.Alias;
            OpenAiActiveKeyPreview.Text = selected.KeyPreview;
            OpenAiActiveOrg.Text = string.IsNullOrEmpty(selected.OrganizationId) ? "" : $"org: {selected.OrganizationId}";
        }
        _suppressOpenAiSelection = false;

        RenderOpenAiUsage();
    }

    private void RenderOpenAiUsage()
    {
        var selected = _openAiAccounts.GetSelected();
        if (selected == null) return;

        var history = _storage.GetOpenAiApiUsageHistory(selected.Id);
        var cutoff = DateTimeOffset.UtcNow.AddDays(-_openAiRangeDays).ToUnixTimeMilliseconds();
        var recent = history.Where(r => r.Timestamp >= cutoff).ToList();

        var grouped = recent
            .GroupBy(r => r.Model)
            .Select(g => new OpenAiModelRow(
                g.Key,
                g.Sum(x => x.InputTokens),
                g.Sum(x => x.OutputTokens),
                g.Sum(x => x.CachedInputTokens),
                g.Sum(x => x.CostUsd)))
            .OrderByDescending(r => r.Cost)
            .ToList();

        OpenAiModelGrid.ItemsSource = grouped;
        OpenAiTotalCost.Text = $"${grouped.Sum(g => g.Cost):F4}";
        OpenAiTotalInput.Text = grouped.Sum(g => g.Input).ToString("N0");
        OpenAiTotalOutput.Text = grouped.Sum(g => g.Output).ToString("N0");
        OpenAiTotalCached.Text = grouped.Sum(g => g.Cached).ToString("N0");
    }

    private void OpenAiAccountCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressOpenAiSelection) return;
        if (OpenAiAccountCombo.SelectedItem is OpenAiAccountDisplay d)
            _openAiAccounts.SelectAccount(d.Account.Id);
    }

    private void OpenAiRangeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (OpenAiRangeCombo?.SelectedItem is ComboBoxItem item &&
            int.TryParse(item.Tag?.ToString(), out var days))
        {
            _openAiRangeDays = days;
            RenderOpenAiUsage();
        }
    }

    private async void OpenAiAddAccountBtn_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenAiAddAccountDialog { Owner = this };
        if (dlg.ShowDialog() != true) return;
        OpenAiStatusLabel.Text = "검증 중...";
        var (ok, err, _) = await _openAiAccounts.AddAccountAsync(dlg.Alias, dlg.ApiKey);
        OpenAiStatusLabel.Text = ok ? "추가 완료" : $"실패: {err}";
        if (ok) await FetchOpenAiUsageAsync();
    }

    private async void OpenAiRefreshBtn_Click(object sender, RoutedEventArgs e)
    {
        await FetchOpenAiUsageAsync();
    }

    private async Task FetchOpenAiUsageAsync()
    {
        var selected = _openAiAccounts.GetSelected();
        if (selected == null) return;
        OpenAiStatusLabel.Text = "조회 중...";
        OpenAiRefreshBtn.IsEnabled = false;
        try
        {
            var end = DateTimeOffset.UtcNow;
            var start = end.AddDays(-_openAiRangeDays);
            var result = await _openAiAccounts.FetchUsageAsync(selected.Id, start, end);
            if (!result.Ok)
            {
                OpenAiStatusLabel.Text = $"실패: {result.Error}";
                return;
            }
            OpenAiStatusLabel.Text = $"갱신 {DateTime.Now:HH:mm:ss} · {result.Buckets.Count} models";
            RenderOpenAiUsage();
        }
        finally
        {
            OpenAiRefreshBtn.IsEnabled = true;
        }
    }

    private void OpenAiRemoveBtn_Click(object sender, RoutedEventArgs e)
    {
        var selected = _openAiAccounts.GetSelected();
        if (selected == null) return;
        var r = System.Windows.MessageBox.Show(this,
            $"'{selected.Alias}' 계정을 제거하시겠습니까?\n관련 사용량 이력도 함께 삭제됩니다.",
            "OpenAI API", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (r != MessageBoxResult.Yes) return;
        _openAiAccounts.RemoveAccount(selected.Id);
    }

    // ──────────────── OpenAI CLI (Codex) tab ────────────────

    private void RefreshCodexUi()
    {
        if (CodexLogPathHint != null)
            CodexLogPathHint.Text = $"{_codex.SessionsDir}\\rollout-*.jsonl 경로를 확인하세요";
        RenderCodexUsage();
    }

    private void RenderCodexUsage()
    {
        if (CodexDashboard == null) return;
        var since = DateTimeOffset.UtcNow.AddDays(-_codexRangeDays);
        var summary = _codex.Aggregate(since);

        if (summary.Models.Count == 0)
        {
            CodexEmptyState.Visibility = Visibility.Visible;
            CodexDashboard.Visibility = Visibility.Collapsed;
            return;
        }

        CodexEmptyState.Visibility = Visibility.Collapsed;
        CodexDashboard.Visibility = Visibility.Visible;
        CodexModelGrid.ItemsSource = summary.Models;
        CodexSessionsCount.Text = summary.SessionsTotal.ToString("N0");
        CodexInputTokens.Text = summary.InputTotal.ToString("N0");
        CodexOutputTokens.Text = summary.OutputTotal.ToString("N0");
        CodexEstCost.Text = $"${summary.CostTotal:F4}";
    }

    private void CodexRangeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CodexRangeCombo?.SelectedItem is ComboBoxItem item &&
            int.TryParse(item.Tag?.ToString(), out var days))
        {
            _codexRangeDays = days;
            RenderCodexUsage();
        }
    }

    private void CodexRefreshBtn_Click(object sender, RoutedEventArgs e)
    {
        RenderCodexUsage();
    }

    // ──────────────── Grok / xAI API tab ────────────────

    private void RefreshGrokUi()
    {
        if (GrokAccountCombo == null) return;
        var accounts = _grokAccounts.GetAccounts();

        if (accounts.Count == 0)
        {
            GrokEmptyState.Visibility = Visibility.Visible;
            GrokDashboard.Visibility = Visibility.Collapsed;
            GrokAccountCombo.ItemsSource = null;
            return;
        }

        GrokEmptyState.Visibility = Visibility.Collapsed;
        GrokDashboard.Visibility = Visibility.Visible;

        _suppressGrokSelection = true;
        GrokAccountCombo.ItemsSource = accounts.Select(a => new GrokAccountDisplay(a)).ToList();
        var selected = _grokAccounts.GetSelected();
        if (selected != null)
        {
            for (int i = 0; i < GrokAccountCombo.Items.Count; i++)
            {
                if (GrokAccountCombo.Items[i] is GrokAccountDisplay d && d.Account.Id == selected.Id)
                {
                    GrokAccountCombo.SelectedIndex = i;
                    break;
                }
            }
            GrokActiveAlias.Text = selected.Alias;
            GrokActiveKeyPreview.Text = selected.KeyPreview;
            GrokActiveMeta.Text = selected.AllowedModels.Count > 0
                ? $"{selected.AllowedModels.Count} allowed models"
                : "";

            GrokKeyName.Text = selected.KeyName ?? "--";
            GrokUserId.Text = selected.UserId ?? "--";
            GrokTeamId.Text = selected.TeamId ?? "--";
            GrokKeyStatus.Text = selected.IsActive ? "ACTIVE" : "DISABLED";
            GrokKeyStatus.Foreground = selected.IsActive
                ? new SolidColorBrush(Color.FromRgb(0x4a, 0xde, 0x80))
                : new SolidColorBrush(Color.FromRgb(0xf8, 0x71, 0x71));
            GrokAllowedModels.ItemsSource = selected.AllowedModels;
        }
        _suppressGrokSelection = false;
    }

    private void GrokAccountCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressGrokSelection) return;
        if (GrokAccountCombo.SelectedItem is GrokAccountDisplay d)
            _grokAccounts.SelectAccount(d.Account.Id);
    }

    private async void GrokAddAccountBtn_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new GrokAddAccountDialog { Owner = this };
        if (dlg.ShowDialog() != true) return;
        GrokStatusLabel.Text = "검증 중...";
        var (ok, err, _) = await _grokAccounts.AddAccountAsync(dlg.Alias, dlg.ApiKey);
        GrokStatusLabel.Text = ok ? "추가 완료" : $"실패: {err}";
    }

    private async void GrokRefreshBtn_Click(object sender, RoutedEventArgs e)
    {
        var selected = _grokAccounts.GetSelected();
        if (selected == null) return;
        GrokStatusLabel.Text = "조회 중...";
        GrokRefreshBtn.IsEnabled = false;
        try
        {
            var (ok, err, _) = await _grokAccounts.RefreshKeyInfoAsync(selected.Id);
            GrokStatusLabel.Text = ok ? $"갱신 {DateTime.Now:HH:mm:ss}" : $"실패: {err}";
            if (ok) RefreshGrokUi();
        }
        finally { GrokRefreshBtn.IsEnabled = true; }
    }

    private void GrokRemoveBtn_Click(object sender, RoutedEventArgs e)
    {
        var selected = _grokAccounts.GetSelected();
        if (selected == null) return;
        var r = System.Windows.MessageBox.Show(this,
            $"'{selected.Alias}' 계정을 제거하시겠습니까?",
            "Grok API", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (r != MessageBoxResult.Yes) return;
        _grokAccounts.RemoveAccount(selected.Id);
    }

    // ──────────────── Grok CLI tab ────────────────

    private void RefreshGrokCliUi()
    {
        if (GrokCliPathHint != null)
            GrokCliPathHint.Text = $"검사 경로: {string.Join(" · ", _grokCli.CandidateDirs)}";
        RenderGrokCliUsage();
    }

    private void RenderGrokCliUsage()
    {
        if (GrokCliDashboard == null) return;
        var since = DateTimeOffset.UtcNow.AddDays(-_grokCliRangeDays);
        var summary = _grokCli.Aggregate(since);

        if (summary.Models.Count == 0)
        {
            GrokCliEmptyState.Visibility = Visibility.Visible;
            GrokCliDashboard.Visibility = Visibility.Collapsed;
            return;
        }

        GrokCliEmptyState.Visibility = Visibility.Collapsed;
        GrokCliDashboard.Visibility = Visibility.Visible;
        GrokCliModelGrid.ItemsSource = summary.Models;
        GrokCliSessionsCount.Text = summary.SessionsTotal.ToString("N0");
        GrokCliInputTokens.Text = summary.InputTotal.ToString("N0");
        GrokCliOutputTokens.Text = summary.OutputTotal.ToString("N0");
        GrokCliEstCost.Text = $"${summary.CostTotal:F4}";
    }

    private void GrokCliRangeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GrokCliRangeCombo?.SelectedItem is ComboBoxItem item &&
            int.TryParse(item.Tag?.ToString(), out var days))
        {
            _grokCliRangeDays = days;
            RenderGrokCliUsage();
        }
    }

    private void GrokCliRefreshBtn_Click(object sender, RoutedEventArgs e)
    {
        RenderGrokCliUsage();
    }
}

internal class GrokAccountDisplay
{
    public GrokApiAccount Account { get; }
    public GrokAccountDisplay(GrokApiAccount a) { Account = a; }
    public override string ToString()
    {
        var pri = Account.IsPrimary ? " ★" : "";
        return $"👤 {Account.Alias}{pri}  ({Account.KeyPreview})";
    }
}

internal class OpenAiAccountDisplay
{
    public OpenAiApiAccount Account { get; }
    public OpenAiAccountDisplay(OpenAiApiAccount a) { Account = a; }
    public override string ToString()
    {
        var pri = Account.IsPrimary ? " ★" : "";
        return $"👤 {Account.Alias}{pri}  ({Account.KeyPreview})";
    }
}

internal class OpenAiModelRow
{
    public string Model { get; }
    public long Input { get; }
    public long Output { get; }
    public long Cached { get; }
    public double Cost { get; }

    public string InputDisplay => Input.ToString("N0");
    public string OutputDisplay => Output.ToString("N0");
    public string CachedDisplay => Cached.ToString("N0");
    public string CostDisplay => $"${Cost:F4}";

    public OpenAiModelRow(string model, long input, long output, long cached, double cost)
    {
        Model = model; Input = input; Output = output; Cached = cached; Cost = cost;
    }
}

internal class AnthropicAccountDisplay
{
    public AnthropicApiAccount Account { get; }
    public AnthropicAccountDisplay(AnthropicApiAccount a) { Account = a; }
    public override string ToString()
    {
        var pri = Account.IsPrimary ? " ★" : "";
        return $"👤 {Account.Alias}{pri}  ({Account.KeyPreview})";
    }
}

internal class AnthropicModelRow
{
    public string Model { get; }
    public long Input { get; }
    public long Output { get; }
    public long CacheWrite { get; }
    public long CacheRead { get; }
    public double Cost { get; }

    public string InputDisplay => Input.ToString("N0");
    public string OutputDisplay => Output.ToString("N0");
    public string CacheWriteDisplay => CacheWrite.ToString("N0");
    public string CacheReadDisplay => CacheRead.ToString("N0");
    public string CostDisplay => $"${Cost:F4}";

    public AnthropicModelRow(string model, long input, long output, long cacheWrite, long cacheRead, double cost)
    {
        Model = model; Input = input; Output = output;
        CacheWrite = cacheWrite; CacheRead = cacheRead; Cost = cost;
    }
}

internal class GeminiAccountDisplay
{
    public GeminiAccount Account { get; }
    public GeminiAccountDisplay(GeminiAccount a) { Account = a; }
    public override string ToString()
    {
        var primary = Account.IsPrimary ? " ★" : "";
        return $"👤 {Account.Alias}  ({Account.KeyPreview}){primary}";
    }
}

internal class GeminiBudgetRow
{
    public string Alias { get; }
    public string SpendText { get; }
    public SolidColorBrush BarBrush { get; }
    public double BarWidth { get; }
    public string SubText { get; }

    public GeminiBudgetRow(GeminiAccount a, double todayCost, double monthCost, double barMaxPx)
    {
        Alias = a.Alias;
        double pct;
        if (a.MonthlyBudgetUsd > 0)
        {
            pct = monthCost / a.MonthlyBudgetUsd;
            SpendText = $"${monthCost:F2} / ${a.MonthlyBudgetUsd:F2}";
            SubText = $"month {pct:P0} · today ${todayCost:F2}";
        }
        else if (a.DailyBudgetUsd > 0)
        {
            pct = todayCost / a.DailyBudgetUsd;
            SpendText = $"${todayCost:F2} / ${a.DailyBudgetUsd:F2}";
            SubText = $"today {pct:P0} · month ${monthCost:F2}";
        }
        else
        {
            pct = 0;
            SpendText = $"${monthCost:F2}";
            SubText = $"no budget · today ${todayCost:F2}";
        }
        BarBrush = PickBrush(pct);
        BarWidth = Math.Max(0, Math.Min(1.0, pct)) * barMaxPx;
    }

    private static SolidColorBrush PickBrush(double pct)
    {
        if (pct >= 1.0) return new SolidColorBrush(Color.FromRgb(0xf8, 0x71, 0x71));
        if (pct >= 0.8) return new SolidColorBrush(Color.FromRgb(0xfb, 0x92, 0x3c));
        if (pct >= 0.6) return new SolidColorBrush(Color.FromRgb(0xfa, 0xcc, 0x15));
        return new SolidColorBrush(Color.FromRgb(0x4a, 0xde, 0x80));
    }
}

internal class TopModelRow
{
    public string Label { get; }
    public string CostText { get; }
    public SolidColorBrush ColorBrush { get; }
    public double BarWidth { get; }
    public string ShareText { get; }

    public TopModelRow(string model, double cost, double barWidth, double share)
    {
        Label = model;
        CostText = $"${cost:F2}";
        BarWidth = Math.Max(0, barWidth);
        ShareText = $"{share:P0}";
        var m = (model ?? "").ToLowerInvariant();
        Color c = m.Contains("flash")
            ? Color.FromRgb(0x4F, 0x7C, 0xE8)
            : m.Contains("pro")
                ? Color.FromRgb(0x9B, 0x72, 0xCB)
                : Color.FromRgb(0x64, 0x74, 0x8b);
        ColorBrush = new SolidColorBrush(c);
    }
}

internal class GeminiCompareRow
{
    public string Label { get; }
    public double Cost { get; }
    public string CostText { get; }
    public double BarWidth { get; set; }

    public GeminiCompareRow(string alias, double cost)
    {
        Label = $"👤 {alias}";
        Cost = cost;
        CostText = $"${cost:F4}";
    }
}

internal class GeminiHistoryItem
{
    public string TimeText { get; }
    public string Model { get; }
    public string InputText { get; }
    public string OutputText { get; }
    public string LatencyText { get; }
    public string CostText { get; }

    public GeminiHistoryItem(GeminiUsageRecord r)
    {
        var t = DateTimeOffset.FromUnixTimeMilliseconds(r.Timestamp).ToLocalTime();
        TimeText = t.ToString("HH:mm:ss");
        Model = r.Model;
        InputText = r.InputTokens.ToString("N0");
        OutputText = r.OutputTokens.ToString("N0");
        LatencyText = $"{r.LatencyMs}";
        CostText = $"${r.CostUsd:F5}";
    }
}
