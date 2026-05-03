using System.Windows;
using System.Windows.Media;
using WpfColor = System.Windows.Media.Color;

namespace AIUsageTracker.Services;

// ════════════════════════════════════════════════════════════════
//  ThemeBrush — 디자인 시스템 토큰 lookup 헬퍼
//  Application.Resources의 SolidColorBrush 키를 런타임에 조회한다.
//  테마 전환 시 자동으로 현재 테마의 색을 반환.
//
//  사용 예:
//    StatusText.Foreground = ThemeBrush.BR("StatusGoodBrush");
//    var c = ThemeBrush.CR("AccentBrush");
// ════════════════════════════════════════════════════════════════
public static class ThemeBrush
{
    private static readonly SolidColorBrush Fallback = new(WpfColor.FromRgb(0x88, 0x88, 0x88));

    /// <summary>현재 테마의 ResourceDictionary에서 brush 키로 조회.</summary>
    public static SolidColorBrush BR(string key) =>
        System.Windows.Application.Current?.Resources[key] as SolidColorBrush ?? Fallback;

    /// <summary>BR과 동일하지만 Color 반환 (애니메이션·스트로크용).</summary>
    public static WpfColor CR(string key) =>
        (System.Windows.Application.Current?.Resources[key] as SolidColorBrush)?.Color ?? Fallback.Color;

    /// <summary>사용량 % 별 신호등 색 (디자인 시스템 표 6번).</summary>
    public static SolidColorBrush UsageColor(double pct) => BR(
        pct >= 1.00 ? "StatusBadBrush"  :
        pct >= 0.80 ? "StatusHighBrush" :
        pct >= 0.60 ? "StatusWarnBrush" :
                      "StatusGoodBrush");
}
