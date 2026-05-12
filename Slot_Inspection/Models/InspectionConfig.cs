namespace Slot_Inspection.Models;

using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>單一相機的裁切 ROI 設定。-1 表示不裁切。</summary>
public sealed class CameraRoi
{
    public string CameraId { get; set; } = "";
    public int X { get; set; } = -1;
    public int Y { get; set; } = -1;
    public int W { get; set; } = -1;
    public int H { get; set; } = -1;

    /// <summary>四個值都有效才啟用裁切。</summary>
    public bool IsEnabled => X >= 0 && Y >= 0 && W > 0 && H > 0;
}

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

    /// <summary>讀碼時 Y 軸移動目標位置 (mm)</summary>
    public double BarcodePositionY { get; set; } = 491.0;

    /// <summary>掃左側條碼時，X 軸位置</summary>
    public double BarcodePositionLeftX { get; set; } = -5.121;

    /// <summary>掃右側條碼時，X 軸位置</summary>
    public double BarcodePositionRightX { get; set; } = 189.922;

    /// <summary>依載台在席位置回傳讀碼器的 X 軸座標</summary>
    public double GetBarcodePositionX(Services.MachineController.CarrierPosition position)
        => position == Services.MachineController.CarrierPosition.Right
            ? BarcodePositionRightX
            : BarcodePositionLeftX;

    // =========================================
    //  相機/光源高度設定（只有 Z 軸，所有 Slot 共用）
    //  TODO: 依實際 teaching 結果調整
    // =========================================

    /// <summary>ZL 下降後的檢測高度（mm），所有 Slot 共用</summary>
    public double CameraHeightZL { get; set; } = 24.7;

    /// <summary>ZR 下降後的檢測高度（mm），所有 Slot 共用</summary>
    public double CameraHeightZR { get; set; } = 0.0;

    /// <summary>ZL/ZR 安全高度（mm）：Y 軸移動前抬升至此位置，避免碰撞</summary>
    public double ZSafeHeight { get; set; } = 0.0;

    // =========================================
    //  裁切 ROI 設定（C1~C4 各自獨立）
    // =========================================

    /// <summary>C1 相機 ROI（X13=true, Right 相機）</summary>
    public CameraRoi RoiC1 { get; set; } = new() { CameraId = "C1" };

    /// <summary>C2 相機 ROI（X13=true, Left 相機）</summary>
    public CameraRoi RoiC2 { get; set; } = new() { CameraId = "C2" };

    /// <summary>C3 相機 ROI（X12=true, Right 相機）</summary>
    public CameraRoi RoiC3 { get; set; } = new() { CameraId = "C3" };

    /// <summary>C4 相機 ROI（X12=true, Left 相機）</summary>
    public CameraRoi RoiC4 { get; set; } = new() { CameraId = "C4" };

    /// <summary>依相機名稱取得對應 ROI。</summary>
    public CameraRoi GetRoi(string cameraId) => cameraId switch
    {
        "C1" => RoiC1,
        "C2" => RoiC2,
        "C3" => RoiC3,
        "C4" => RoiC4,
        _    => new CameraRoi()
    };

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

        int idx = name.LastIndexOf('_');
        if (idx >= 0 && idx < name.Length - 1)
        {
            string key = name[(idx + 1)..].Trim();
            if (!string.IsNullOrWhiteSpace(key))
                return key;
        }

        // 檔名沒有底線 → 使用預設 JSON key
        return DefaultAlgJsonKey;
    }

    // =========================================
    //  設定檔存讀（JSON）
    // =========================================

    private static readonly string ConfigFilePath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "Config", "InspectionConfig.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    /// <summary>將目前設定儲存至 Config\InspectionConfig.json。</summary>
    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigFilePath)!);
        File.WriteAllText(ConfigFilePath, JsonSerializer.Serialize(this, JsonOpts));
    }

    /// <summary>從 Config\InspectionConfig.json 載入設定，檔案不存在時回傳預設值。</summary>
    public static InspectionConfig Load()
    {
        if (!File.Exists(ConfigFilePath))
            return new InspectionConfig();
        try
        {
            var json = File.ReadAllText(ConfigFilePath);
            return JsonSerializer.Deserialize<InspectionConfig>(json, JsonOpts) ?? new InspectionConfig();
        }
        catch
        {
            return new InspectionConfig();
        }
    }
}
