using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AIUsageTracker.Models;
using AIUsageTracker.Services;

namespace AIUsageTracker.Views;

public partial class GeminiAccountManagerWindow : Window
{
    private readonly GeminiAccountService _accounts;
    private GeminiAccount? _current;

    public GeminiAccountManagerWindow(GeminiAccountService accounts)
    {
        InitializeComponent();
        _accounts = accounts;
        RefreshList();
    }

    private void RefreshList()
    {
        var prevId = _current?.Id;
        AccountList.ItemsSource = _accounts.GetAccounts()
            .Select(a => new ListRow(a)).ToList();

        if (prevId != null)
        {
            var idx = _accounts.GetAccounts().ToList().FindIndex(a => a.Id == prevId);
            if (idx >= 0) AccountList.SelectedIndex = idx;
        }
        else if (AccountList.Items.Count > 0)
        {
            AccountList.SelectedIndex = 0;
        }
        else
        {
            _current = null;
            ClearEditor();
        }
    }

    private void AccountList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AccountList.SelectedItem is not ListRow row) { _current = null; ClearEditor(); return; }
        _current = row.Account;
        LoadIntoEditor(row.Account);
    }

    private void LoadIntoEditor(GeminiAccount a)
    {
        AliasBox.Text = a.Alias;
        KeyPreviewText.Text = a.KeyPreview;
        PrimaryBadge.Visibility = a.IsPrimary ? Visibility.Visible : Visibility.Collapsed;
        NewKeyBox.Password = "";
        DailyBudgetBox.Text = a.DailyBudgetUsd > 0 ? a.DailyBudgetUsd.ToString("F2") : "0";
        MonthlyBudgetBox.Text = a.MonthlyBudgetUsd > 0 ? a.MonthlyBudgetUsd.ToString("F2") : "0";
        AlertThresholdBox.Text = a.AlertThresholdPct.ToString();
        PrimaryCheck.IsChecked = a.IsPrimary;
        ActiveCheck.IsChecked = a.IsActive;
        StatusLabel.Text = "";
    }

    private void ClearEditor()
    {
        AliasBox.Text = "";
        KeyPreviewText.Text = "--";
        PrimaryBadge.Visibility = Visibility.Collapsed;
        NewKeyBox.Password = "";
        DailyBudgetBox.Text = "0";
        MonthlyBudgetBox.Text = "0";
        AlertThresholdBox.Text = "80";
        PrimaryCheck.IsChecked = false;
        ActiveCheck.IsChecked = false;
        StatusLabel.Text = "";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_current == null) return;

        var alias = AliasBox.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(alias))
        {
            SetStatus("별칭이 비어있습니다", "#f87171");
            return;
        }

        if (!double.TryParse(DailyBudgetBox.Text, out var daily) || daily < 0) daily = 0;
        if (!double.TryParse(MonthlyBudgetBox.Text, out var monthly) || monthly < 0) monthly = 0;
        if (!int.TryParse(AlertThresholdBox.Text, out var thr)) thr = 80;

        _accounts.RenameAccount(_current.Id, alias);
        _accounts.SetBudget(_current.Id, daily, monthly, thr);
        _accounts.SetActive(_current.Id, ActiveCheck.IsChecked == true);

        if (PrimaryCheck.IsChecked == true && !_current.IsPrimary)
            _accounts.SetPrimary(_current.Id);

        SetStatus("저장됨", "#4ade80");
        RefreshList();
    }

    private async void RotateKey_Click(object sender, RoutedEventArgs e)
    {
        if (_current == null) return;
        var newKey = NewKeyBox.Password?.Trim() ?? "";
        if (string.IsNullOrEmpty(newKey))
        {
            SetStatus("새 API Key를 입력해주세요", "#f87171");
            return;
        }

        SetStatus("검증 중...", "#facc15");
        var (ok, err) = await _accounts.RotateKeyAsync(_current.Id, newKey);
        if (ok)
        {
            SetStatus("Key 교체 완료", "#4ade80");
            NewKeyBox.Password = "";
            RefreshList();
        }
        else
        {
            SetStatus($"실패: {err}", "#f87171");
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_current == null) return;
        var res = System.Windows.MessageBox.Show(this,
            $"'{_current.Alias}' 계정과 사용 기록을 모두 삭제하시겠습니까?",
            "계정 삭제", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (res != MessageBoxResult.Yes) return;

        _accounts.RemoveAccount(_current.Id);
        _current = null;
        RefreshList();
        SetStatus("삭제됨", "#888");
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void SetStatus(string text, string colorHex)
    {
        StatusLabel.Text = text;
        StatusLabel.Foreground = new SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(colorHex));
    }

    private class ListRow
    {
        public GeminiAccount Account { get; }
        public ListRow(GeminiAccount a) { Account = a; }
        public override string ToString()
        {
            var primary = Account.IsPrimary ? " ★" : "";
            var inactive = Account.IsActive ? "" : " (off)";
            return $"👤 {Account.Alias}{primary}{inactive}\n   {Account.KeyPreview}";
        }
    }
}
