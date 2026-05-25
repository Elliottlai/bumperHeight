namespace Slot_Inspection.Models;

using System.IO;
using System.Text.Json;

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
        new(Y: 44.722), new(Y: 54.722), new(Y: 64.722), new(Y: 74.722), new(Y: 84.722),
        new(Y: 94.722), new(Y: 104.722), new(Y: 114.722), new(Y: 124.722), new(Y: 134.722),
        new(Y: 144.722), new(Y: 154.722), new(Y: 164.722), new(Y: 174.722), new(Y: 184.722),
        new(Y: 194.722), new(Y: 204.722), new(Y: 214.722), new(Y: 224.722), new(Y: 234.722),
        new(Y: 244.722), new(Y: 254.722), new(Y: 264.722), new(Y: 274.722), new(Y: 284.722),
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

    // =========================================
    //  設定檔存讀（JSON）
    // =========================================

    private static readonly string SlotFilePath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "Config", "SlotPositions.json");

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private sealed class SlotPositionsData
    {
        public double[] AreaA_Row1 { get; set; } = [];
        public double[] AreaA_Row2 { get; set; } = [];
        public double[] AreaB_Row1 { get; set; } = [];
        public double[] AreaB_Row2 { get; set; } = [];
    }

    /// <summary>將目前所有 Slot Y 座標儲存至 Config\SlotPositions.json。</summary>
    public static void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SlotFilePath)!);
        var data = new SlotPositionsData
        {
            AreaA_Row1 = AreaA_Row1.Select(s => s.Y).ToArray(),
            AreaA_Row2 = AreaA_Row2.Select(s => s.Y).ToArray(),
            AreaB_Row1 = AreaB_Row1.Select(s => s.Y).ToArray(),
            AreaB_Row2 = AreaB_Row2.Select(s => s.Y).ToArray(),
        };
        File.WriteAllText(SlotFilePath, JsonSerializer.Serialize(data, JsonOpts));
    }

    /// <summary>從 Config\SlotPositions.json 載入，覆蓋靜態陣列。檔案不存在則保留預設值。</summary>
    public static void Load()
    {
        if (!File.Exists(SlotFilePath)) return;
        try
        {
            var data = JsonSerializer.Deserialize<SlotPositionsData>(
                File.ReadAllText(SlotFilePath), JsonOpts);
            if (data == null) return;

            static void Apply(ref SlotPosition[] arr, double[] vals)
            {
                for (int i = 0; i < arr.Length && i < vals.Length; i++)
                    arr[i] = new SlotPosition(vals[i]);
            }

            Apply(ref AreaA_Row1, data.AreaA_Row1);
            Apply(ref AreaA_Row2, data.AreaA_Row2);
            Apply(ref AreaB_Row1, data.AreaB_Row1);
            Apply(ref AreaB_Row2, data.AreaB_Row2);
        }
        catch { /* 讀取失敗保留預設值 */ }
    }
}
