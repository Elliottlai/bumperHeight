using System.Globalization;
using System.Windows.Data;
using Slot_Inspection.Services;

namespace Slot_Inspection.Helper;

/// <summary>
/// 把 MainViewModel.ActiveAlarms（目前生效中的異常原因清單）轉成 tooltip 文字，
/// 讓操作者/工程師滑鼠移到狀態燈上就能看到「為什麼在亮紅燈」，不用回頭翻 log。
/// 沒有任何異常時回傳 null，WPF 對 null tooltip 不會顯示，也不會擋住其他狀態的滑鼠提示。
/// </summary>
public sealed class AlarmListToTooltipConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not IReadOnlyList<AlarmEntry> alarms || alarms.Count == 0)
            return null;

        return string.Join(Environment.NewLine,
            alarms.Select(a => $"[{a.RaisedAt:HH:mm:ss}] {a.Code}：{a.Message}"));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
