namespace DeltaAxis_RS485.Models;

/// <summary>
/// 對應 Slot_Inspection 主程式 Config\SlotPositions.json 裡，目前唯一啟用的Y軸座標表
/// （AreaA_Row2/AreaB_Row1/AreaB_Row2 在主程式裡尚未啟用、固定為0，故不納入安全參考）。
/// </summary>
public sealed class SlotPositionsFile
{
    public double[]? AreaA_Row1_R { get; set; }
    public double[]? AreaA_Row1_L { get; set; }
}

/// <summary>
/// 對應 Slot_Inspection 主程式 Config\InspectionConfig.json 裡，跟Z軸下降深度/取料位置相關的欄位子集。
/// </summary>
public sealed class InspectionConfigFile
{
    public double BarcodePositionY { get; set; }

    // ── Z 軸「下降到底」的檢測高度（下限，數字越大代表越往下）──
    public double CameraHeightZL_R { get; set; }
    public double CameraHeightZL_L { get; set; }
    public double CameraHeightZR_R { get; set; }
    public double CameraHeightZR_L { get; set; }

    // ── 主程式 Y 軸移動前要求 ZL/ZR 必須先退回的安全高度（數字越小代表退得越上面）。R/L 兩組。──
    public double ZLSafeHeight_R { get; set; }
    public double ZLSafeHeight_L { get; set; }
    public double ZRSafeHeight_R { get; set; }
    public double ZRSafeHeight_L { get; set; }

    /// <summary>主程式判斷「已在安全高度」的容差（mm）。實際高度 ≤ SafeHeight + Tolerance 視為安全。</summary>
    public double ZSafeTolerance { get; set; } = 1.0;
}

/// <summary>
/// 從 Slot_Inspection 設定檔讀取、彙整出來的安全參考快照。
/// DeltaAxis_RS485 沒有PLC、無法判斷目前是R板還是L板，故R/L兩組都檢查，取較保守（較容易先觸發）的門檻。
/// </summary>
public sealed class SlotInspectionSafetySnapshot
{
    /// <summary>已知安全的Y軸位置（25個Slot的R組+L組 + 取料位置），調整Z軸前Y軸應落在其中一點上。</summary>
    public List<double> KnownYPositions { get; init; } = [];

    /// <summary>ZL軸下降深度上限（R組、L組取較小值，較保守）。</summary>
    public double ZLCameraHeightLimit { get; init; }

    /// <summary>ZR軸下降深度上限（R組、L組取較小值，較保守）。</summary>
    public double ZRCameraHeightLimit { get; init; }

    /// <summary>
    /// ZL 需退到的安全高度上限（R組、L組取較小值＝較嚴，較保守）。
    /// Y 軸移動前，ZL 實際高度必須低於 此值 + <see cref="ZSafeTolerance"/>。
    /// 0 代表主程式該欄位尚未 teaching（呼叫端應改用自己的保守預設值）。
    /// </summary>
    public double ZLSafeHeightCeiling { get; init; }

    /// <summary>ZR 需退到的安全高度上限（R組、L組取較小值＝較嚴，較保守）。語意同 <see cref="ZLSafeHeightCeiling"/>。</summary>
    public double ZRSafeHeightCeiling { get; init; }

    /// <summary>主程式判斷「已在安全高度」的容差（mm）。</summary>
    public double ZSafeTolerance { get; init; } = 1.0;

    /// <summary>這份快照實際是從哪個 Config 資料夾讀出來的（給UI顯示、除錯用）。</summary>
    public string SourceFolder { get; init; } = "";

    /// <summary>讀取時間，給UI顯示用。</summary>
    public DateTime LoadedAt { get; init; } = DateTime.Now;
}
