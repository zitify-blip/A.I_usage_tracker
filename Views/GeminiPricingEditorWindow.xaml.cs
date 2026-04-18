using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using AIUsageTracker.Models;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using AIUsageTracker.Services;
using AIUsageTracker.Services.Providers;

namespace AIUsageTracker.Views;

public partial class GeminiPricingEditorWindow : Window
{
    private readonly StorageService _storage;
    private readonly List<PricingRow> _rows = new();

    public GeminiPricingEditorWindow(StorageService storage)
    {
        InitializeComponent();
        _storage = storage;
        BuildRows();
        RowsHost.ItemsSource = _rows;
    }

    private void BuildRows()
    {
        _rows.Clear();
        var overrides = _storage.GetPricingOverrides()
            .ToDictionary(p => p.ModelId, p => p, StringComparer.OrdinalIgnoreCase);

        foreach (var preset in GeminiPricing.Models)
        {
            var ov = overrides.GetValueOrDefault(preset.ModelId);
            _rows.Add(new PricingRow(preset, ov));
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        int saved = 0, removed = 0, errors = 0;
        foreach (var row in _rows)
        {
            var inputEmpty = string.IsNullOrWhiteSpace(row.InputText);
            var outputEmpty = string.IsNullOrWhiteSpace(row.OutputText);
            var cacheEmpty = string.IsNullOrWhiteSpace(row.CacheText);

            if (inputEmpty && outputEmpty && cacheEmpty)
            {
                _storage.RemovePricingOverride(row.Preset.ModelId);
                row.SetStatus("프리셋", "#888");
                removed++;
                continue;
            }

            if (!TryParse(row.InputText, row.Preset.InputPricePerMTok, out var inp) ||
                !TryParse(row.OutputText, row.Preset.OutputPricePerMTok, out var outp) ||
                !TryParse(row.CacheText, row.Preset.CachePricePerMTok, out var cac))
            {
                row.SetStatus("형식 오류", "#f87171");
                errors++;
                continue;
            }

            _storage.SetPricingOverride(row.Preset.ModelId, inp, outp, cac);
            row.SetStatus("저장됨", "#4ade80");
            saved++;
        }

        StatusLabel.Text = errors > 0
            ? $"저장 {saved}건 · 제거 {removed}건 · 오류 {errors}건"
            : $"저장 {saved}건 · 제거 {removed}건";
        StatusLabel.Foreground = new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString(errors > 0 ? "#f87171" : "#4ade80"));
    }

    private void ResetAll_Click(object sender, RoutedEventArgs e)
    {
        var res = System.Windows.MessageBox.Show(this,
            "모든 단가를 프리셋으로 되돌리시겠습니까?",
            "단가 복원", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (res != MessageBoxResult.Yes) return;

        foreach (var row in _rows)
        {
            _storage.RemovePricingOverride(row.Preset.ModelId);
            row.InputText = "";
            row.OutputText = "";
            row.CacheText = "";
            row.SetStatus("프리셋", "#888");
        }
        StatusLabel.Text = "모두 프리셋으로 복원됨";
        StatusLabel.Foreground = new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString("#4ade80"));
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private static bool TryParse(string? text, double fallback, out double value)
    {
        if (string.IsNullOrWhiteSpace(text)) { value = fallback; return true; }
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && value >= 0) return true;
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) && value >= 0) return true;
        value = 0;
        return false;
    }

    public class PricingRow : INotifyPropertyChanged
    {
        public GeminiModelPrice Preset { get; }
        public string DisplayName => Preset.DisplayName;

        private string _input = "";
        private string _output = "";
        private string _cache = "";
        private string _statusText = "";
        private Brush _statusBrush = Brushes.Gray;

        public string InputText { get => _input; set { _input = value; OnChanged(nameof(InputText)); } }
        public string OutputText { get => _output; set { _output = value; OnChanged(nameof(OutputText)); } }
        public string CacheText { get => _cache; set { _cache = value; OnChanged(nameof(CacheText)); } }
        public string StatusText { get => _statusText; private set { _statusText = value; OnChanged(nameof(StatusText)); } }
        public Brush StatusBrush { get => _statusBrush; private set { _statusBrush = value; OnChanged(nameof(StatusBrush)); } }

        public PricingRow(GeminiModelPrice preset, GeminiPricingOverride? ov)
        {
            Preset = preset;
            if (ov != null)
            {
                _input = ov.InputPricePerMTok.ToString("G", CultureInfo.InvariantCulture);
                _output = ov.OutputPricePerMTok.ToString("G", CultureInfo.InvariantCulture);
                _cache = ov.CachePricePerMTok.ToString("G", CultureInfo.InvariantCulture);
                SetStatus("오버라이드", "#facc15");
            }
            else
            {
                SetStatus($"프리셋 ({preset.InputPricePerMTok}/{preset.OutputPricePerMTok}/{preset.CachePricePerMTok})", "#666");
            }
        }

        public void SetStatus(string text, string colorHex)
        {
            StatusText = text;
            StatusBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnChanged(string p) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    }
}
