namespace Slot_Inspection.Models;

/// <summary>
/// 每個 Slot 對應的軸座標。
/// ?? TODO：在真機上教點後，填入實際座標。
/// </summary>
public sealed record SlotPosition(double X, double Y, double Z, double R = 0);

/// <summary>
/// Slot 座標表 — 集中管理所有區域的拍照位置。
/// 目前全部填 0（Stub），不移軸也能跑通流程。
/// </summary>
public static class SlotPositionTable
{
    // ── Area A Row1：Slot 1~13 ──
    // ?? TODO：用手持示教器把每個 Slot 的位置記下來填進來
    public static readonly SlotPosition[] AreaA_Row1 = Enumerable
        .Range(0, 13)
        .Select(i => new SlotPosition(X: 0, Y: 0, Z: 0))
        .ToArray();

    // ── Area A Row2：Slot 14~25 ──
    public static readonly SlotPosition[] AreaA_Row2 = Enumerable
        .Range(0, 12)
        .Select(i => new SlotPosition(X: 0, Y: 0, Z: 0))
        .ToArray();

    // ── Area B Row1：Slot 1~13 ──
    public static readonly SlotPosition[] AreaB_Row1 = Enumerable
        .Range(0, 13)
        .Select(i => new SlotPosition(X: 0, Y: 0, Z: 0))
        .ToArray();

    // ── Area B Row2：Slot 14~25 ──
    public static readonly SlotPosition[] AreaB_Row2 = Enumerable
        .Range(0, 12)
        .Select(i => new SlotPosition(X: 0, Y: 0, Z: 0))
        .ToArray();

    /// <summary>
    /// 依 target / slotIndex 取得對應座標。
    /// </summary>
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
