using System.Windows;
using AIUsageTracker.Services;
using Microsoft.Web.WebView2.Core;

namespace AIUsageTracker.Views;

public partial class LoginWindow : Window
{
    private static readonly string WebView2UserDataFolder = ClaudeApiService.WebView2UserDataFolder;

    public bool LoginSuccess { get; private set; }
    public string? FetchResultJson { get; private set; }

    public LoginWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await InitWebView();
    }

    private async Task InitWebView()
    {
        try
        {
            System.IO.Directory.CreateDirectory(WebView2UserDataFolder);
            var env = await CoreWebView2Environment.CreateAsync(null, WebView2UserDataFolder);
            await LoginWebView.EnsureCoreWebView2Async(env);

            // Restrict navigation to claude.ai only
            LoginWebView.CoreWebView2.NavigationStarting += (_, args) =>
            {
                if (args.Uri != null &&
                    !args.Uri.StartsWith("https://claude.ai/", StringComparison.OrdinalIgnoreCase) &&
                    !args.Uri.StartsWith("https://accounts.google.com/", StringComparison.OrdinalIgnoreCase) &&
                    !args.Uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
                {
                    args.Cancel = true;
                }
            };

            LoginWebView.CoreWebView2.Navigate("https://claude.ai/login");
            StatusText.Text = "로딩 중...";

            LoginWebView.CoreWebView2.NavigationCompleted += (_, _) =>
            {
                StatusText.Text = "";
            };
        }
        catch (Exception ex)
        {
            Services.Logger.Error("LoginWindow WebView init failed", ex);
            StatusText.Text = "WebView 초기화 실패";
        }
    }

    private async void DoneBtn_Click(object sender, RoutedEventArgs e)
    {
        if (LoginWebView.CoreWebView2 == null) return;

        DoneBtn.IsEnabled = false;
        StatusText.Text = "사용량 조회 중...";

        try
        {
            // Use WebMessageReceived to get async results reliably
            var tcs = new TaskCompletionSource<string>();

            void MsgHandler(object? s, CoreWebView2WebMessageReceivedEventArgs args)
            {
                tcs.TrySetResult(args.WebMessageAsJson);
                LoginWebView.CoreWebView2.WebMessageReceived -= MsgHandler;
            }
            LoginWebView.CoreWebView2.WebMessageReceived += MsgHandler;

            await LoginWebView.CoreWebView2.ExecuteScriptAsync(ClaudeApiService.FetchViaPostMessage);

            // Wait for message (max 30s)
            var completed = await Task.WhenAny(tcs.Task, Task.Delay(30000));

            if (completed != tcs.Task)
            {
                LoginWebView.CoreWebView2.WebMessageReceived -= MsgHandler;
                StatusText.Text = "시간 초과 — 다시 시도하세요";
                DoneBtn.IsEnabled = true;
                return;
            }

            var resultJson = tcs.Task.Result;

            // Unescape if needed
            if (resultJson != null && resultJson.StartsWith("\"") && resultJson.EndsWith("\""))
            {
                resultJson = System.Text.Json.JsonSerializer.Deserialize<string>(resultJson) ?? resultJson;
            }

            var result = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(resultJson!);
            var ok = result.TryGetProperty("ok", out var okProp) && okProp.GetBoolean();

            if (ok)
            {
                LoginSuccess = true;
                FetchResultJson = resultJson;
                StatusText.Text = "성공!";
                await Task.Delay(300);
                DialogResult = true;
                Close();
            }
            else
            {
                var error = result.TryGetProperty("error", out var errProp) ? errProp.GetString() : "unknown";
                var status = result.TryGetProperty("status", out var stProp) ? stProp.ToString() : "";
                StatusText.Text = $"실패: {error} {status} — 로그인 후 다시 시도하세요";
                DoneBtn.IsEnabled = true;
            }
        }
        catch (Exception ex)
        {
            Services.Logger.Error("LoginWindow fetch failed", ex);
            StatusText.Text = "데이터 조회 실패 — 다시 시도하세요";
            DoneBtn.IsEnabled = true;
        }
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
