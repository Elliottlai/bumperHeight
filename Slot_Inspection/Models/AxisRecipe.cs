namespace Slot_Inspection.Models;

/// <summary>
/// Y/ZL/ZR 軸（各含 R組/L組）的一組命名參數組合，可獨立於即時生效設定存讀。
/// </summary>
public sealed class AxisRecipeData
{
    public double[] YPositions_R { get; set; } = new double[25];
    public double[] YPositions_L { get; set; } = new double[25];

    public double CameraHeightZL_R { get; set; }
    public double CameraHeightZL_L { get; set; }
    public double CameraHeightZR_R { get; set; }
    public double CameraHeightZR_L { get; set; }

    public double ZLSafeHeight_R { get; set; }
    public double ZLSafeHeight_L { get; set; }
    public double ZRSafeHeight_R { get; set; }
    public double ZRSafeHeight_L { get; set; }
}
