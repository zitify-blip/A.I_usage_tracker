using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using Clipboard = System.Windows.Clipboard;
using Color = System.Windows.Media.Color;
using MessageBox = System.Windows.MessageBox;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using TextBox = System.Windows.Controls.TextBox;

namespace AIUsageTracker.Views;

public partial class GeminiRelayHelpWindow : Window
{
    private readonly string _baseUrl;
    private readonly string _trackerKey;
    private readonly DispatcherTimer _statusTimer;

    // Managed env var names (User scope)
    private static readonly string[] ManagedVars =
    {
        "GOOGLE_API_KEY",
        "GEMINI_API_KEY",
        "GOOGLE_GENAI_BASE_URL",
        "GOOGLE_GEMINI_BASE_URL"
    };

    // Backup prefix for foreign values we overwrite
    private const string BackupPrefix = "AI_TRACKER_BACKUP_";

    // WM_SETTINGCHANGE broadcast for env var propagation
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd, uint Msg, UIntPtr wParam, string lParam,
        uint fuFlags, uint uTimeout, out UIntPtr lpdwResult);

    private const int HWND_BROADCAST = 0xffff;
    private const uint WM_SETTINGCHANGE = 0x001A;
    private const uint SMTO_ABORTIFHUNG = 0x0002;

    public GeminiRelayHelpWindow(int port, string alias)
    {
        InitializeComponent();

        _baseUrl = $"http://127.0.0.1:{port}";
        _trackerKey = $"tracker-{alias}";

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
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

        RefreshStatus();
    }

    private string ExpectedValue(string varName) => varName.EndsWith("_KEY") ? _trackerKey : _baseUrl;

    private enum VarState { Missing, Match, Foreign }

    private VarState GetVarState(string varName)
    {
        var current = Environment.GetEnvironmentVariable(varName, EnvironmentVariableTarget.User);
        if (string.IsNullOrEmpty(current)) return VarState.Missing;
        return current == ExpectedValue(varName) ? VarState.Match : VarState.Foreign;
    }

    private void RefreshStatus()
    {
        int match = 0, missing = 0, foreign = 0;
        foreach (var v in ManagedVars)
        {
            switch (GetVarState(v))
            {
                case VarState.Match: match++; break;
                case VarState.Missing: missing++; break;
                case VarState.Foreign: foreign++; break;
            }
        }

        if (match == ManagedVars.Length)
        {
            StatusBadgeText.Text = "✓ 적용됨 — 환경변수 4개 모두 트래커로 향함";
            StatusBadgeText.Foreground = B("#4ade80");
            PrimaryActionBtn.Visibility = Visibility.Collapsed;
            RevertBtn.Visibility = Visibility.Visible;
        }
        else if (missing == ManagedVars.Length)
        {
            StatusBadgeText.Text = "⚪ 미적용 — 설정된 환경변수 없음";
            StatusBadgeText.Foreground = B("#9ca3af");
            PrimaryActionBtn.Content = "⚡ 자동 설정";
            PrimaryActionBtn.Visibility = Visibility.Visible;
            RevertBtn.Visibility = Visibility.Collapsed;
        }
        else if (foreign > 0)
        {
            StatusBadgeText.Text = $"⚠ 다른 값 감지됨 ({foreign}/{ManagedVars.Length}) — 적용 시 백업 후 교체";
            StatusBadgeText.Foreground = B("#facc15");
            PrimaryActionBtn.Content = "⚡ 백업 후 자동 설정";
            PrimaryActionBtn.Visibility = Visibility.Visible;
            RevertBtn.Visibility = foreign == 0 ? Visibility.Collapsed : Visibility.Visible;
        }
        else
        {
            StatusBadgeText.Text = $"⚠ 부분 적용 ({match}/{ManagedVars.Length})";
            StatusBadgeText.Foreground = B("#facc15");
            PrimaryActionBtn.Content = "⚡ 마저 적용하기";
            PrimaryActionBtn.Visibility = Visibility.Visible;
            RevertBtn.Visibility = Visibility.Visible;
        }
    }

    private static SolidColorBrush B(string hex)
    {
        var c = (Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
        return new SolidColorBrush(c);
    }

    private void PrimaryAction_Click(object sender, RoutedEventArgs e)
    {
        int foreignCount = 0;
        foreach (var v in ManagedVars)
            if (GetVarState(v) == VarState.Foreign) foreignCount++;

        if (foreignCount > 0)
        {
            var res = MessageBox.Show(
                $"현재 환경변수 중 {foreignCount}개가 트래커가 아닌 다른 값으로 설정되어 있습니다.\n\n" +
                $"기존 값을 백업({BackupPrefix}…)한 뒤 트래커 값으로 교체합니다.\n되돌리기 시 자동 복원됩니다.\n\n진행할까요?",
                "환경변수 덮어쓰기 확인",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);
            if (res != MessageBoxResult.OK) return;
        }

        try
        {
            foreach (var v in ManagedVars)
            {
                var current = Environment.GetEnvironmentVariable(v, EnvironmentVariableTarget.User);
                var expected = ExpectedValue(v);

                if (!string.IsNullOrEmpty(current) && current != expected)
                {
                    // back up only if we don't already have a backup (keep oldest non-tracker value)
                    var backupName = BackupPrefix + v;
                    var existingBackup = Environment.GetEnvironmentVariable(backupName, EnvironmentVariableTarget.User);
                    if (string.IsNullOrEmpty(existingBackup))
                    {
                        Environment.SetEnvironmentVariable(backupName, current, EnvironmentVariableTarget.User);
                    }
                }

                Environment.SetEnvironmentVariable(v, expected, EnvironmentVariableTarget.User);
            }

            BroadcastEnvironmentChange();
            RefreshStatus();
            FlashStatus("✓ 자동 설정 완료 · 새로 여는 프로세스에 자동 반영됩니다", ok: true);
        }
        catch (Exception ex)
        {
            FlashStatus($"설정 실패: {ex.Message}", ok: false);
        }
    }

    private void Revert_Click(object sender, RoutedEventArgs e)
    {
        var res = MessageBox.Show(
            "환경변수를 원래 상태로 되돌립니다.\n\n" +
            "• 백업된 값이 있으면 복원합니다.\n" +
            "• 백업이 없으면 변수를 제거합니다.\n\n진행할까요?",
            "되돌리기 확인",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question);
        if (res != MessageBoxResult.OK) return;

        try
        {
            foreach (var v in ManagedVars)
            {
                var backupName = BackupPrefix + v;
                var backup = Environment.GetEnvironmentVariable(backupName, EnvironmentVariableTarget.User);

                if (!string.IsNullOrEmpty(backup))
                {
                    Environment.SetEnvironmentVariable(v, backup, EnvironmentVariableTarget.User);
                    Environment.SetEnvironmentVariable(backupName, null, EnvironmentVariableTarget.User);
                }
                else
                {
                    Environment.SetEnvironmentVariable(v, null, EnvironmentVariableTarget.User);
                }
            }

            BroadcastEnvironmentChange();
            RefreshStatus();
            FlashStatus("✓ 되돌리기 완료 · 새로 여는 프로세스는 원래 상태로 복귀합니다", ok: true);
        }
        catch (Exception ex)
        {
            FlashStatus($"되돌리기 실패: {ex.Message}", ok: false);
        }
    }

    private static void BroadcastEnvironmentChange()
    {
        SendMessageTimeout(
            new IntPtr(HWND_BROADCAST),
            WM_SETTINGCHANGE,
            UIntPtr.Zero,
            "Environment",
            SMTO_ABORTIFHUNG,
            5000,
            out _);
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

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
