namespace Slot_Inspection.Models;

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
}
