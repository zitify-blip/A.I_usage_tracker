using System.Text.Json;
using AIUsageTracker.Models;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace AIUsageTracker.Services;

/// <summary>
/// Uses a hidden WebView2 navigated to claude.ai to make same-origin API calls.
/// Shares the same UserDataFolder as LoginWindow so cookies persist automatically.
/// Uses postMessage to reliably receive async fetch results.
/// </summary>
public class ClaudeApiService
{
    public static readonly string WebView2UserDataFolder = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AIUsageTracker", "WebView2Data");

    public static readonly string FetchViaPostMessage = @"
(function() {
  var noCacheHeaders = { 'Accept': 'application/json', 'Cache-Control': 'no-cache', 'Pragma': 'no-cache' };
  var ts = Date.now();
  fetch('https://claude.ai/api/organizations?_t=' + ts, { credentials: 'include', cache: 'no-store', headers: noCacheHeaders })
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
        fetch('https://claude.ai/api/organizations/' + orgId + '/usage?_t=' + Date.now(), {
          credentials: 'include',
          cache: 'no-store',
          headers: noCacheHeaders
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

        // Restrict navigation to claude.ai only
        _webView.CoreWebView2.NavigationStarting += (_, args) =>
        {
            if (args.Uri != null &&
                !args.Uri.StartsWith("https://claude.ai/", StringComparison.OrdinalIgnoreCase) &&
                !args.Uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
            {
                args.Cancel = true;
            }
        };

        var tcs = new TaskCompletionSource<bool>();
        void Handler(object? s, CoreWebView2DOMContentLoadedEventArgs e)
        {
            _ready = true;
            tcs.TrySetResult(true);
            _webView.CoreWebView2.DOMContentLoaded -= Handler;
        }
        _webView.CoreWebView2.DOMContentLoaded += Handler;

        _webView.CoreWebView2.Navigate("https://claude.ai/");

        // Wait for page to load (max 15s)
        if (await Task.WhenAny(tcs.Task, Task.Delay(15000)) != tcs.Task)
            _webView.CoreWebView2.DOMContentLoaded -= Handler;
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
        if (await Task.WhenAny(tcs.Task, Task.Delay(15000)) != tcs.Task)
            _webView.CoreWebView2.DOMContentLoaded -= Handler;

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
                Logger.Warn($"API fetch returned error: {error}");
                return (false, error, null);
            }
        }
        catch (JsonException ex)
        {
            Logger.Error("Usage API response parse failed", ex);
            return (false, "parse_failed", null);
        }
        catch (Exception ex)
        {
            Logger.Error("Usage API request failed", ex);
            return (false, "request_failed", null);
        }
    }

    public async Task<(bool ok, string? error)> SendMessageAsync(string message)
    {
        if (_webView?.CoreWebView2 == null || !_ready)
            return (false, "webview_not_ready");

        try
        {
            var tcs = new TaskCompletionSource<string>();

            void MsgHandler(object? s, CoreWebView2WebMessageReceivedEventArgs args)
            {
                tcs.TrySetResult(args.WebMessageAsJson);
                _webView.CoreWebView2.WebMessageReceived -= MsgHandler;
            }
            _webView.CoreWebView2.WebMessageReceived += MsgHandler;

            var script = $@"
(function() {{
  var msg = {JsonSerializer.Serialize(message)};
  var tz = (Intl.DateTimeFormat().resolvedOptions().timeZone) || 'UTC';
  fetch('https://claude.ai/api/organizations', {{ credentials: 'include' }})
    .then(function(r) {{ return r.json(); }})
    .then(function(orgs) {{
      var orgId = orgs[0].uuid;
      var convUuid = crypto.randomUUID();
      fetch('https://claude.ai/api/organizations/' + orgId + '/chat_conversations', {{
        method: 'POST', credentials: 'include',
        headers: {{ 'Content-Type': 'application/json' }},
        body: JSON.stringify({{ uuid: convUuid, name: '' }})
      }})
        .then(function(r) {{
          if (!r.ok) throw new Error('create_conv_' + r.status);
          return r.json();
        }})
        .then(function(conv) {{
          fetch('https://claude.ai/api/organizations/' + orgId + '/chat_conversations/' + conv.uuid + '/completion', {{
            method: 'POST', credentials: 'include',
            headers: {{ 'Content-Type': 'application/json', 'Accept': 'text/event-stream' }},
            body: JSON.stringify({{
              prompt: msg,
              parent_message_uuid: '00000000-0000-4000-8000-000000000000',
              timezone: tz,
              personalized_styles: [], tools: [], attachments: [], files: [], sync_sources: [],
              rendering_mode: 'messages'
            }})
          }}).then(function(r) {{
            window.chrome.webview.postMessage({{ ok: r.ok, status: r.status, conv: conv.uuid }});
          }}).catch(function(e) {{
            window.chrome.webview.postMessage({{ ok: false, error: 'completion_failed', message: String(e) }});
          }});
        }})
        .catch(function(e) {{
          window.chrome.webview.postMessage({{ ok: false, error: 'create_conv_failed', message: String(e) }});
        }});
    }})
    .catch(function(e) {{
      window.chrome.webview.postMessage({{ ok: false, error: 'orgs_failed', message: String(e) }});
    }});
}})()";

            await _webView.CoreWebView2.ExecuteScriptAsync(script);

            var completed = await Task.WhenAny(tcs.Task, Task.Delay(30000));
            if (completed != tcs.Task)
            {
                _webView.CoreWebView2.WebMessageReceived -= MsgHandler;
                return (false, "timeout");
            }

            var resultJson = tcs.Task.Result;
            if (resultJson.StartsWith("\"") && resultJson.EndsWith("\""))
                resultJson = JsonSerializer.Deserialize<string>(resultJson) ?? resultJson;

            var result = JsonSerializer.Deserialize<JsonElement>(resultJson);
            var ok = result.TryGetProperty("ok", out var okProp) && okProp.GetBoolean();
            if (ok) return (true, null);

            var error = result.TryGetProperty("error", out var errProp) ? errProp.GetString() : "unknown";
            Logger.Warn($"SendMessage returned error: {error}");
            return (false, error);
        }
        catch (Exception ex)
        {
            Logger.Error("SendMessage failed", ex);
            return (false, "exception");
        }
    }
}
