namespace DeltaAxis_RS485.Models;

/// <summary>
/// PR mode 運動參數設定值
/// </summary>
public class MotionSettings
{
    /// <summary>目標速度 (rpm)，對應 P5.060 speed #1</summary>
    public int TargetSpeed { get; set; } = 1000;

    /// <summary>加速時間 (ms)，對應 P5.020</summary>
    public int AccelerationTime { get; set; } = 200;

    /// <summary>減速時間 (ms)，對應 P5.020</summary>
    public int DecelerationTime { get; set; } = 200;

    /// <summary>延遲時間 (ms)，對應 P5.040</summary>
    public int DelayTime { get; set; } = 0;

    /// <summary>到位逾時時間 (ms)</summary>
    public int InPositionTimeout { get; set; } = 5000;

    /// <summary>每 mm 對應的 PUU 數量（需依機構換算）</summary>
    public double PuuPerMm { get; set; } = 10000.0;
}