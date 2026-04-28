namespace Slot_Inspection.Models;

using System.IO;

/// <summary>
/// Inspection parameters (light, exposure, NG threshold).
/// </summary>
public sealed class InspectionConfig
{
    // -- Light (Left camera, CH1) --
    public int LightChannelLeft { get; set; } = 1;
    public int LightIntensityLeft { get; set; } = 50;

    // -- Light (Right camera, CH2) --
    public int LightChannelRight { get; set; } = 2;
    public int LightIntensityRight { get; set; } = 50;

    public int LightStabilizeMs { get; set; } = 50;

    // -- Camera --
    public int CaptureWaitMs { get; set; } = 100;
    public bool SaveImages { get; set; } = true;
    public string ImageSavePath { get; set; } = @"D:\InspectionImages";

    // -- NG threshold --
    public double NgThresholdLow { get; set; } = 0.40;
    public double NgThresholdHigh { get; set; } = 0.60;

    // -- Axis move timeout --
    public TimeSpan MoveTimeout { get; set; } = TimeSpan.FromSeconds(30);

    // -- Simulation images --
    /// <summary>
    /// Test image folder path. Supports *.png / *.jpg / *.bmp / *.tif.
    /// Images are cycled; if folder is empty or missing, colored block images are generated instead.
    /// </summary>
    public string SimImageFolderPath { get; set; } = @"D:\TestImages";

    // =========================================
    //  Barcode 讀碼軸設定（只有 X 軸移動）
    //  TODO: 依實際 teaching 結果調整
    // =========================================

    /// <summary>掃左側條碼時，X 軸位置</summary>
    public double BarcodePositionLeftX { get; set; } = 100.0;

    /// <summary>掃右側條碼時，X 軸位置</summary>
    public double BarcodePositionRightX { get; set; } = 300.0;

    /// <summary>依載台在席位置回傳讀碼器的 X 軸座標</summary>
    public double GetBarcodePositionX(Services.MachineController.CarrierPosition position)
        => position == Services.MachineController.CarrierPosition.Right
            ? BarcodePositionRightX
            : BarcodePositionLeftX;

    // =========================================
    //  相機/光源高度設定（只有 Z 軸，所有 Slot 共用）
    //  TODO: 依實際 teaching 結果調整
    // =========================================

    /// <summary>相機拍攝高度（Z 軸），所有 Slot 共用</summary>
    public double CameraHeightZ { get; set; } = 50.0;

    // =========================================
    //  BumperFlat 演算法 JSON 設定
    // =========================================

    /// <summary>
    /// 演算法預設 JSON Key（對應 Config 資料夾下的 .json 檔名）。
    /// 例如 "Parameter" → Config/Parameter.json
    /// </summary>
    public string DefaultAlgJsonKey { get; set; } = "Parameter";

    /// <summary>
    /// 依 Slot 位置取得對應的演算法 JSON Key。
    /// 目前所有 Slot 共用同一組參數；
    /// 若未來需要 per-slot 設定，可改回傳 $"{target}_S{slotIndex + 1}"。
    /// </summary>
    public string GetAlgJsonKey(
        SlotInspectionProgress.TargetCollection target,
        int slotIndex)
    {
        return DefaultAlgJsonKey;
    }

    /// <summary>
    /// 根據影像檔名推導演算法 JSON key（比照 TestALG）：
    /// 1) 先取檔名（無副檔名）
    /// 2) 若含 '_'，取最後一個 '_' 後面的字串
    /// 3) 回傳空值時 fallback DefaultAlgJsonKey
    /// </summary>
    public string GetAlgJsonKeyFromImagePath(string imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
            return DefaultAlgJsonKey;

        string name = Path.GetFileNameWithoutExtension(imagePath) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
            return DefaultAlgJsonKey;

        string key = name;
        int idx = name.LastIndexOf('_');
        if (idx >= 0 && idx < name.Length - 1)
            key = name[(idx + 1)..].Trim();

        return string.IsNullOrWhiteSpace(key) ? DefaultAlgJsonKey : key;
    }
}
