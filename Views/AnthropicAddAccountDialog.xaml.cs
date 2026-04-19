using System.Windows;

namespace AIUsageTracker.Views;

public partial class AnthropicAddAccountDialog : Window
{
    public string Alias { get; private set; } = "";
    public string ApiKey { get; private set; } = "";

    public AnthropicAddAccountDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => AliasBox.Focus();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var key = ApiKeyBox.Password?.Trim() ?? "";
        if (string.IsNullOrEmpty(key))
        {
            System.Windows.MessageBox.Show(this, "API Key를 입력해주세요.", "Claude API",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            ApiKeyBox.Focus();
            return;
        }
        Alias = AliasBox.Text?.Trim() ?? "";
        ApiKey = key;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
