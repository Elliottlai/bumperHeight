namespace Slot_Inspection.Models;

/// <summary>
/// 檢測參數設定（光源、曝光、NG 閾值）。
/// 集中管理，方便日後做成 Setting 頁面。
/// </summary>
public sealed class InspectionConfig
{
    // ── 光源 ──
    /// <summary>光源通道（OPT 光控箱 CH1~CH4）?? TODO：確認接線</summary>
    public int LightChannel { get; set; } = 1;
    /// <summary>拍照時的光源亮度（0~100%）?? TODO：實際調試</summary>
    public int LightIntensity { get; set; } = 50;
    /// <summary>開光後等待穩定的毫秒數</summary>
    public int LightStabilizeMs { get; set; } = 50;

    // ── 相機 ──
    /// <summary>等待曝光完成的毫秒數 ?? TODO：依曝光時間調整</summary>
    public int CaptureWaitMs { get; set; } = 100;
    /// <summary>是否存圖（開發時開，量產時可關）</summary>
    public bool SaveImages { get; set; } = true;
    /// <summary>存圖根目錄</summary>
    public string ImageSavePath { get; set; } = @"D:\InspectionImages";

    // ── NG 閾值 ──
    /// <summary>量測值低於此值 → NG ?? TODO：依規格書調整</summary>
    public double NgThresholdLow { get; set; } = 0.40;
    /// <summary>量測值高於此值 → NG ?? TODO：依規格書調整</summary>
    public double NgThresholdHigh { get; set; } = 0.60;

    // ── 移軸超時 ──
    public TimeSpan MoveTimeout { get; set; } = TimeSpan.FromSeconds(30);
}
