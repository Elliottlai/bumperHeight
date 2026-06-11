namespace Slot_Inspection.Models;

using System.IO;
using System.Text.Json;

/// <summary>
/// 記錄每個 Slot 巡檢時 Y 軸座標。
/// X 軸由讀碼參數決定，Z 軸由 InspectionConfig.CameraHeightZ 統一管理。
/// </summary>
public sealed record SlotPosition(double Y);

/// <summary>
/// Slot 座標表。管理所有區域 Slot 的 Y 軸座標。
/// AreaA_Row1 分 R 組（X12=true，板子在右側）和 L 組（X13=true，板子在左側）兩套，
/// 執行時依當前 X12/X13 PLC 訊號自動選取對應的 Y 座標。
/// </summary>
public static class SlotPositionTable
{
    // ── Area A Row1：R 組（X12=true，板子在右側）Slot 1~25 ──
    public static SlotPosition[] AreaA_Row1_R =
    [
        new(Y: 44.722), new(Y: 54.722), new(Y: 64.722), new(Y: 74.722), new(Y: 84.722),
        new(Y: 94.722), new(Y: 104.722), new(Y: 114.722), new(Y: 124.722), new(Y: 134.722),
        new(Y: 144.722), new(Y: 154.722), new(Y: 164.722), new(Y: 174.722), new(Y: 184.722),
        new(Y: 194.722), new(Y: 204.722), new(Y: 214.722), new(Y: 224.722), new(Y: 234.722),
        new(Y: 244.722), new(Y: 254.722), new(Y: 264.722), new(Y: 274.722), new(Y: 284.722),
    ];

    // ── Area A Row1：L 組（X13=true，板子在左側）Slot 1~25 ──
    public static SlotPosition[] AreaA_Row1_L =
    [
        new(Y: 44.722), new(Y: 54.722), new(Y: 64.722), new(Y: 74.722), new(Y: 84.722),
        new(Y: 94.722), new(Y: 104.722), new(Y: 114.722), new(Y: 124.722), new(Y: 134.722),
        new(Y: 144.722), new(Y: 154.722), new(Y: 164.722), new(Y: 174.722), new(Y: 184.722),
        new(Y: 194.722), new(Y: 204.722), new(Y: 214.722), new(Y: 224.722), new(Y: 234.722),
        new(Y: 244.722), new(Y: 254.722), new(Y: 264.722), new(Y: 274.722), new(Y: 284.722),
    ];

    // ── 向後相容屬性：AreaA_Row1 → R 組（舊程式碼不會報錯）──
    public static SlotPosition[] AreaA_Row1
    {
        get => AreaA_Row1_R;
        set => AreaA_Row1_R = value;
    }

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

    /// <summary>
    /// 依 target / slotIndex 取得該 Slot 的 Y 軸座標（向後相容，固定使用 R 組）。
    /// 新程式碼請改用 GetByCarrier。
    /// </summary>
    public static SlotPosition Get(SlotInspectionProgress.TargetCollection target, int slotIndex)
        => GetByCarrier(target, slotIndex, isR: true);

    /// <summary>
    /// 依 target / slotIndex / 載台訊號取得該 Slot 的 Y 軸座標。
    /// isR=true（X12 訊號，R 載台）→ R 組；isR=false（X13 訊號，L 載台）→ L 組。
    /// </summary>
    public static SlotPosition GetByCarrier(
        SlotInspectionProgress.TargetCollection target, int slotIndex, bool isR)
    {
        return target switch
        {
            SlotInspectionProgress.TargetCollection.AreaA_Row1
                => isR ? AreaA_Row1_R[slotIndex] : AreaA_Row1_L[slotIndex],
            SlotInspectionProgress.TargetCollection.AreaA_Row2 => AreaA_Row2[slotIndex],
            SlotInspectionProgress.TargetCollection.AreaB_Row1 => AreaB_Row1[slotIndex],
            SlotInspectionProgress.TargetCollection.AreaB_Row2 => AreaB_Row2[slotIndex],
            _ => throw new ArgumentOutOfRangeException(nameof(target))
        };
    }

    // =========================================
    //  設定檔存讀（JSON）
    // =========================================

    private static readonly string SlotFilePath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "Config", "SlotPositions.json");

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private sealed class SlotPositionsData
    {
        // 新格式：R/L 兩組
        public double[]? AreaA_Row1_R { get; set; }
        public double[]? AreaA_Row1_L { get; set; }
        // 舊格式：向後相容（首次升級時 R/L 均沿用舊值）
        public double[]? AreaA_Row1   { get; set; }
        public double[]  AreaA_Row2   { get; set; } = [];
        public double[]  AreaB_Row1   { get; set; } = [];
        public double[]  AreaB_Row2   { get; set; } = [];
    }

    /// <summary>將目前所有 Slot Y 座標（R/L 兩組）儲存至 Config\SlotPositions.json。</summary>
    public static void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SlotFilePath)!);
        var data = new SlotPositionsData
        {
            AreaA_Row1_R = AreaA_Row1_R.Select(s => s.Y).ToArray(),
            AreaA_Row1_L = AreaA_Row1_L.Select(s => s.Y).ToArray(),
            AreaA_Row2   = AreaA_Row2.Select(s => s.Y).ToArray(),
            AreaB_Row1   = AreaB_Row1.Select(s => s.Y).ToArray(),
            AreaB_Row2   = AreaB_Row2.Select(s => s.Y).ToArray(),
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

            static void Apply(ref SlotPosition[] arr, double[]? vals)
            {
                if (vals == null) return;
                for (int i = 0; i < arr.Length && i < vals.Length; i++)
                    arr[i] = new SlotPosition(vals[i]);
            }

            // 優先讀取新格式 R/L；若不存在則沿用舊 AreaA_Row1 欄位（向後相容）
            if (data.AreaA_Row1_R != null)
                Apply(ref AreaA_Row1_R, data.AreaA_Row1_R);
            else
                Apply(ref AreaA_Row1_R, data.AreaA_Row1);

            if (data.AreaA_Row1_L != null)
                Apply(ref AreaA_Row1_L, data.AreaA_Row1_L);
            else
                Apply(ref AreaA_Row1_L, data.AreaA_Row1);

            Apply(ref AreaA_Row2, data.AreaA_Row2);
            Apply(ref AreaB_Row1, data.AreaB_Row1);
            Apply(ref AreaB_Row2, data.AreaB_Row2);
        }
        catch { /* 讀取失敗保留預設值 */ }
    }
}