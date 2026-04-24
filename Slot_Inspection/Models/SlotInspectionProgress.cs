using System.Windows.Media;

namespace Slot_Inspection.Models;

/// <summary>
/// S03 inspection loop progress report per Slot.
/// </summary>
public sealed class SlotInspectionProgress
{
    public enum TargetCollection { AreaA_Row1, AreaA_Row2, AreaB_Row1, AreaB_Row2 }

    public TargetCollection Target { get; init; }
    public int SlotIndex { get; init; }
    public double Value { get; init; }
    public bool IsNg { get; init; }
    public string StatusText { get; init; } = "";

    /// <summary>Captured image for UI display (nullable)</summary>
    public ImageSource? Image { get; init; }
}
