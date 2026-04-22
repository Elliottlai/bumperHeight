namespace Slot_Inspection.Models;

/// <summary>
/// S03 檢測迴圈中，每完成一個 Slot 就回報一次進度。
/// </summary>
public sealed class SlotInspectionProgress
{
    /// <summary>對應 ViewModel 中哪一個 ObservableCollection</summary>
    public enum TargetCollection { AreaA_Row1, AreaA_Row2, AreaB_Row1, AreaB_Row2 }

    public TargetCollection Target { get; init; }

    /// <summary>Collection 內的索引（0-based）</summary>
    public int SlotIndex { get; init; }

    /// <summary>量測值</summary>
    public double Value { get; init; }

    /// <summary>是否 NG</summary>
    public bool IsNg { get; init; }

    /// <summary>顯示在 Footer 的狀態文字</summary>
    public string StatusText { get; init; } = "";
}
