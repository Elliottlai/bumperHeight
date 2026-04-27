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

    // -- Barcode reader axis positions (TODO: 依實際 teaching 結果調整) --
    public (double X, double Y, double Z) BarcodePositionLeft { get; set; } = (100.0, 0.0, 50.0);
    public (double X, double Y, double Z) BarcodePositionRight { get; set; } = (300.0, 0.0, 50.0);

    /// <summary>
    /// 依載台在席位置回傳讀碼器的軸座標。
    /// Left 或 Both 預設先掃左邊。
    /// </summary>
    public (double X, double Y, double Z) GetBarcodePosition(
        Services.MachineController.CarrierPosition position) => position switch
    {
        Services.MachineController.CarrierPosition.Right => BarcodePositionRight,
        _ => BarcodePositionLeft,
    };
}
