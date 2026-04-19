using System.Windows;

namespace AIUsageTracker.Views;

public partial class SessionResetDialog : Window
{
    public string Message { get; private set; } = "";

    public SessionResetDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => { MessageBox.Focus(); MessageBox.SelectAll(); };
    }

    private void Send_Click(object sender, RoutedEventArgs e)
    {
        var msg = MessageBox.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(msg))
        {
            System.Windows.MessageBox.Show(this, "메시지를 입력해주세요.", "세션 리셋",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            MessageBox.Focus();
            return;
        }
        Message = msg;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
