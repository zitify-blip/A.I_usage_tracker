using System.Windows;
using ClaudeUsageTracker.Services;
using Microsoft.Web.WebView2.Core;

namespace ClaudeUsageTracker.Views;

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

            LoginWebView.CoreWebView2.Navigate("https://claude.ai/login");
            StatusText.Text = "로딩 중...";

            LoginWebView.CoreWebView2.NavigationCompleted += (_, _) =>
            {
                StatusText.Text = "";
            };
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
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

            // Inject script that fetches with absolute URL and posts result back
            await LoginWebView.CoreWebView2.ExecuteScriptAsync(@"
(function() {
  fetch('https://claude.ai/api/organizations', { credentials: 'include' })
    .then(function(orgsRes) {
      if (orgsRes.status === 401 || orgsRes.status === 403) {
        window.chrome.webview.postMessage({ ok: false, error: 'not_logged_in', status: orgsRes.status });
        return;
      }
      if (!orgsRes.ok) {
        window.chrome.webview.postMessage({ ok: false, error: 'orgs_failed', status: orgsRes.status });
        return;
      }
      orgsRes.json().then(function(orgs) {
        if (!orgs || !orgs.length) {
          window.chrome.webview.postMessage({ ok: false, error: 'no_org' });
          return;
        }
        var orgId = orgs[0].uuid;
        fetch('https://claude.ai/api/organizations/' + orgId + '/usage', {
          credentials: 'include',
          headers: { 'Accept': 'application/json' }
        }).then(function(usageRes) {
          if (!usageRes.ok) {
            window.chrome.webview.postMessage({ ok: false, error: 'usage_failed', status: usageRes.status });
            return;
          }
          usageRes.json().then(function(usage) {
            window.chrome.webview.postMessage({ ok: true, data: JSON.stringify(usage) });
          });
        });
      });
    })
    .catch(function(e) {
      window.chrome.webview.postMessage({ ok: false, error: 'exception', message: String(e) });
    });
})()");

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
            StatusText.Text = $"오류: {ex.Message}";
            DoneBtn.IsEnabled = true;
        }
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
