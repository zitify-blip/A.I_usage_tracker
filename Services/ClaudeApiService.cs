using System.Text.Json;
using ClaudeUsageTracker.Models;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace ClaudeUsageTracker.Services;

/// <summary>
/// Uses a hidden WebView2 navigated to claude.ai to make same-origin API calls.
/// Shares the same UserDataFolder as LoginWindow so cookies persist automatically.
/// Uses postMessage to reliably receive async fetch results.
/// </summary>
public class ClaudeApiService
{
    public static readonly string WebView2UserDataFolder = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ClaudeUsageTracker", "WebView2Data");

    private static readonly string FetchViaPostMessage = @"
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
          if (usageRes.status === 401 || usageRes.status === 403) {
            window.chrome.webview.postMessage({ ok: false, error: 'not_logged_in', status: usageRes.status });
            return;
          }
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
})()";

    private WebView2? _webView;
    private bool _ready;

    public bool IsReady => _ready;

    public async Task InitializeAsync(WebView2 webView)
    {
        _webView = webView;
        _ready = false;

        System.IO.Directory.CreateDirectory(WebView2UserDataFolder);
        var env = await CoreWebView2Environment.CreateAsync(null, WebView2UserDataFolder);
        await _webView.EnsureCoreWebView2Async(env);

        _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        _webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
        _webView.CoreWebView2.Settings.IsStatusBarEnabled = false;

        var tcs = new TaskCompletionSource<bool>();
        _webView.CoreWebView2.DOMContentLoaded += (_, _) =>
        {
            _ready = true;
            tcs.TrySetResult(true);
        };

        _webView.CoreWebView2.Navigate("https://claude.ai/");

        // Wait for page to load (max 15s)
        await Task.WhenAny(tcs.Task, Task.Delay(15000));
    }

    public async Task ReloadAsync()
    {
        if (_webView?.CoreWebView2 == null) return;
        _ready = false;

        var tcs = new TaskCompletionSource<bool>();
        void Handler(object? s, CoreWebView2DOMContentLoadedEventArgs e)
        {
            _ready = true;
            tcs.TrySetResult(true);
            _webView.CoreWebView2.DOMContentLoaded -= Handler;
        }
        _webView.CoreWebView2.DOMContentLoaded += Handler;

        _webView.CoreWebView2.Navigate("https://claude.ai/");
        await Task.WhenAny(tcs.Task, Task.Delay(15000));

        // Extra wait for JS/cookies to settle
        await Task.Delay(1500);
    }

    public async Task<(bool ok, string? error, UsageApiResponse? data)> FetchUsageAsync()
    {
        if (_webView?.CoreWebView2 == null || !_ready)
            return (false, "webview_not_ready", null);

        try
        {
            var tcs = new TaskCompletionSource<string>();

            void MsgHandler(object? s, CoreWebView2WebMessageReceivedEventArgs args)
            {
                tcs.TrySetResult(args.WebMessageAsJson);
                _webView.CoreWebView2.WebMessageReceived -= MsgHandler;
            }
            _webView.CoreWebView2.WebMessageReceived += MsgHandler;

            await _webView.CoreWebView2.ExecuteScriptAsync(FetchViaPostMessage);

            // Wait for postMessage result (max 30s)
            var completed = await Task.WhenAny(tcs.Task, Task.Delay(30000));
            if (completed != tcs.Task)
            {
                _webView.CoreWebView2.WebMessageReceived -= MsgHandler;
                return (false, "timeout", null);
            }

            var resultJson = tcs.Task.Result;

            // WebMessageAsJson returns JSON-encoded, unescape if needed
            if (resultJson.StartsWith("\"") && resultJson.EndsWith("\""))
            {
                resultJson = JsonSerializer.Deserialize<string>(resultJson) ?? resultJson;
            }

            var result = JsonSerializer.Deserialize<JsonElement>(resultJson);
            var ok = result.TryGetProperty("ok", out var okProp) && okProp.GetBoolean();

            if (ok)
            {
                var dataStr = result.GetProperty("data").GetString();
                if (dataStr == null) return (false, "no_data", null);

                var usage = JsonSerializer.Deserialize<UsageApiResponse>(dataStr,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                return (true, null, usage);
            }
            else
            {
                var error = result.TryGetProperty("error", out var errProp) ? errProp.GetString() : "unknown";
                return (false, error, null);
            }
        }
        catch (Exception ex)
        {
            return (false, ex.Message, null);
        }
    }
}
