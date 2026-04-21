using System;
using System.Windows;
using System.Windows.Threading;
using Clipboard = System.Windows.Clipboard;
using Color = System.Windows.Media.Color;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using TextBox = System.Windows.Controls.TextBox;

namespace AIUsageTracker.Views;

public partial class GeminiRelayHelpWindow : Window
{
    private readonly string _baseUrl;
    private readonly string _trackerKey;
    private readonly DispatcherTimer _statusTimer;

    public GeminiRelayHelpWindow(int port, string alias)
    {
        InitializeComponent();

        _baseUrl = $"http://127.0.0.1:{port}";
        _trackerKey = $"tracker-{alias}";

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        _statusTimer.Tick += (_, _) => { StatusText.Text = ""; _statusTimer.Stop(); };

        SubtitleText.Text = $"· Port {port}  ·  Key: {_trackerKey}";

        BaseUrlBox.Text = $"Base URL : {_baseUrl}\r\nAPI Key  : {_trackerKey}";

        EnvBox.Text =
            "# 이 스니펫을 본인 PowerShell 창에 붙여넣으면\r\n" +
            "# 해당 창에 즉시 적용 + 사용자 환경변수에도 영구 저장됩니다.\r\n" +
            "$vars = @{\r\n" +
            $"    GOOGLE_API_KEY         = '{_trackerKey}'\r\n" +
            $"    GEMINI_API_KEY         = '{_trackerKey}'\r\n" +
            $"    GOOGLE_GENAI_BASE_URL  = '{_baseUrl}'\r\n" +
            $"    GOOGLE_GEMINI_BASE_URL = '{_baseUrl}'\r\n" +
            "}\r\n" +
            "foreach ($k in $vars.Keys) {\r\n" +
            "    Set-Item \"env:$k\" $vars[$k]\r\n" +
            "    [Environment]::SetEnvironmentVariable($k, $vars[$k], 'User')\r\n" +
            "}";

        EnvCmdBox.Text =
            "REM 현재 cmd.exe 창에 즉시 적용 (set) + 영구 저장 (setx) 쌍\r\n" +
            $"set GOOGLE_API_KEY={_trackerKey}\r\n" +
            $"setx GOOGLE_API_KEY {_trackerKey}\r\n" +
            $"set GEMINI_API_KEY={_trackerKey}\r\n" +
            $"setx GEMINI_API_KEY {_trackerKey}\r\n" +
            $"set GOOGLE_GENAI_BASE_URL={_baseUrl}\r\n" +
            $"setx GOOGLE_GENAI_BASE_URL {_baseUrl}\r\n" +
            $"set GOOGLE_GEMINI_BASE_URL={_baseUrl}\r\n" +
            $"setx GOOGLE_GEMINI_BASE_URL {_baseUrl}";

        CurlBox.Text =
            $"curl -X POST \"{_baseUrl}/v1beta/models/gemini-2.5-flash:generateContent?key={_trackerKey}\" ^\r\n" +
            $"  -H \"Content-Type: application/json\" ^\r\n" +
            $"  -d \"{{\\\"contents\\\":[{{\\\"parts\\\":[{{\\\"text\\\":\\\"hi\\\"}}]}}]}}\"";

        PyLegacyBox.Text =
            "# pip install google-generativeai\r\n" +
            "import google.generativeai as genai\r\n" +
            "\r\n" +
            $"genai.configure(\r\n" +
            $"    api_key=\"{_trackerKey}\",\r\n" +
            $"    transport=\"rest\",\r\n" +
            $"    client_options={{\"api_endpoint\": \"{_baseUrl}\"}},\r\n" +
            ")\r\n" +
            "\r\n" +
            "model = genai.GenerativeModel(\"gemini-2.5-flash\")\r\n" +
            "print(model.generate_content(\"hi\").text)";

        PyNewBox.Text =
            "# pip install google-genai\r\n" +
            "from google import genai\r\n" +
            "from google.genai import types\r\n" +
            "\r\n" +
            $"client = genai.Client(\r\n" +
            $"    api_key=\"{_trackerKey}\",\r\n" +
            $"    http_options=types.HttpOptions(base_url=\"{_baseUrl}\"),\r\n" +
            ")\r\n" +
            "\r\n" +
            "resp = client.models.generate_content(model=\"gemini-2.5-flash\", contents=\"hi\")\r\n" +
            "print(resp.text)";

        NodeBox.Text =
            "// npm i @google/generative-ai\r\n" +
            "import { GoogleGenerativeAI } from \"@google/generative-ai\";\r\n" +
            "\r\n" +
            $"const genAI = new GoogleGenerativeAI(\"{_trackerKey}\");\r\n" +
            $"const model = genAI.getGenerativeModel(\r\n" +
            $"    {{ model: \"gemini-2.5-flash\" }},\r\n" +
            $"    {{ baseUrl: \"{_baseUrl}\" }}\r\n" +
            $");\r\n" +
            "\r\n" +
            "const r = await model.generateContent(\"hi\");\r\n" +
            "console.log(r.response.text());";
    }

    private void Copy(TextBox box, string label)
    {
        try
        {
            Clipboard.SetText(box.Text);
            FlashStatus($"✓ {label} 복사됨", ok: true);
        }
        catch (Exception ex)
        {
            FlashStatus($"복사 실패: {ex.Message}", ok: false);
        }
    }

    private void FlashStatus(string text, bool ok)
    {
        StatusText.Text = text;
        StatusText.Foreground = new SolidColorBrush(
            ok ? Color.FromRgb(0x4a, 0xde, 0x80)
               : Color.FromRgb(0xf8, 0x71, 0x71));
        _statusTimer.Stop();
        _statusTimer.Start();
    }

    private void CopyBaseUrl_Click(object sender, RoutedEventArgs e) => Copy(BaseUrlBox, "Base URL");
    private void CopyEnv_Click(object sender, RoutedEventArgs e) => Copy(EnvBox, "PowerShell 스니펫");
    private void CopyEnvCmd_Click(object sender, RoutedEventArgs e) => Copy(EnvCmdBox, "cmd.exe 스니펫");
    private void CopyCurl_Click(object sender, RoutedEventArgs e) => Copy(CurlBox, "cURL");
    private void CopyPyLegacy_Click(object sender, RoutedEventArgs e) => Copy(PyLegacyBox, "Python (legacy)");
    private void CopyPyNew_Click(object sender, RoutedEventArgs e) => Copy(PyNewBox, "Python (genai)");
    private void CopyNode_Click(object sender, RoutedEventArgs e) => Copy(NodeBox, "Node.js");

    private void CopyAll_Click(object sender, RoutedEventArgs e)
    {
        var all =
            $"# A.I. Usage Tracker — Gemini Local Relay\r\n" +
            $"# Base URL : {_baseUrl}\r\n" +
            $"# API Key  : {_trackerKey}\r\n" +
            $"\r\n" +
            $"# ─── Env vars · PowerShell (current + persist) ───\r\n{EnvBox.Text}\r\n\r\n" +
            $"# ─── Env vars · cmd.exe (set + setx) ───\r\n{EnvCmdBox.Text}\r\n\r\n" +
            $"# ─── cURL ───\r\n{CurlBox.Text}\r\n\r\n" +
            $"# ─── Python (google-generativeai) ───\r\n{PyLegacyBox.Text}\r\n\r\n" +
            $"# ─── Python (google-genai) ───\r\n{PyNewBox.Text}\r\n\r\n" +
            $"// ─── Node.js (@google/generative-ai) ───\r\n{NodeBox.Text}\r\n";

        try
        {
            Clipboard.SetText(all);
            FlashStatus("✓ 전체 사용법이 클립보드에 복사됨", ok: true);
        }
        catch (Exception ex)
        {
            FlashStatus($"복사 실패: {ex.Message}", ok: false);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
