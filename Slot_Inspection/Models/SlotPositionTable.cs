namespace Slot_Inspection.Models;

/// <summary>
/// 每個 Slot 的載台 Y 軸位置。
/// X 軸用於讀碼（不參與 Slot 檢測），Z 軸由 InspectionConfig.CameraHeightZ 統一控制。
/// </summary>
public sealed record SlotPosition(double Y);

/// <summary>
/// Slot 座標表 — 管理所有區域各 Slot 的 Y 軸座標。
/// TODO: 在機台上 Teaching 後，將實際 Y 座標填入。
/// </summary>
public static class SlotPositionTable
{
    // ── Area A Row1：Slot 1~25 ──
    public static SlotPosition[] AreaA_Row1 =
    [
        //new(Y: 53.79),new(Y: 341.790), //for testing

        new(Y: 53.79),new(Y: 65.79),new(Y: 77.79),new(Y: 89.79),new(Y: 101.79),new(Y: 113.79),
        new(Y: 125.79),new(Y: 137.79),new(Y: 149.79),new(Y: 161.79),new(Y: 173.79),new(Y: 185.79),
        new(Y: 197.79),new(Y: 209.79),new(Y: 221.79),new(Y: 233.79),new(Y: 245.79),new(Y: 257.79),
        new(Y: 269.79),new(Y: 281.79),new(Y: 293.79),new(Y: 305.79),new(Y: 317.79),new(Y: 329.79),
        new(Y: 341.79),

    ];

    // ── Area A Row2：Slot 14~25 ──
    public static SlotPosition[] AreaA_Row2 =
    [
        new(Y: 0), new(Y: 0), new(Y: 0), new(Y: 0), new(Y: 0),
        new(Y: 0), new(Y: 0), new(Y: 0), new(Y: 0), new(Y: 0),
        new(Y: 0), new(Y: 0),
    ];

    // ── Area B Row1：Slot 1~13 ──
    public static SlotPosition[] AreaB_Row1 =
    [
        new(Y: 0), new(Y: 0), new(Y: 0), new(Y: 0), new(Y: 0),
        new(Y: 0), new(Y: 0), new(Y: 0), new(Y: 0), new(Y: 0),
        new(Y: 0), new(Y: 0), new(Y: 0),
    ];

    // ── Area B Row2：Slot 14~25 ──
    public static SlotPosition[] AreaB_Row2 =
    [
        new(Y: 0), new(Y: 0), new(Y: 0), new(Y: 0), new(Y: 0),
        new(Y: 0), new(Y: 0), new(Y: 0), new(Y: 0), new(Y: 0),
        new(Y: 0), new(Y: 0),
    ];

    /// <summary>依 target / slotIndex 取得該 Slot 的 Y 軸座標</summary>
    public static SlotPosition Get(SlotInspectionProgress.TargetCollection target, int slotIndex)
        => target switch
        {
            SlotInspectionProgress.TargetCollection.AreaA_Row1 => AreaA_Row1[slotIndex],
            SlotInspectionProgress.TargetCollection.AreaA_Row2 => AreaA_Row2[slotIndex],
            SlotInspectionProgress.TargetCollection.AreaB_Row1 => AreaB_Row1[slotIndex],
            SlotInspectionProgress.TargetCollection.AreaB_Row2 => AreaB_Row2[slotIndex],
            _ => throw new ArgumentOutOfRangeException(nameof(target))
        };
}
