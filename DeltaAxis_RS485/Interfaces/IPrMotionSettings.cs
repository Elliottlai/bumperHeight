namespace DeltaAxis_RS485.Interfaces;

/// <summary>
/// PR mode 運動參數設定介面
/// </summary>
public interface IPrMotionSettings
{
    /// <summary>目標速度 (rpm)，對應 P5.060 speed #1</summary>
    int TargetSpeed { get; set; }

    /// <summary>加速時間 (ms)，對應 P5.020 acc/dec #1</summary>
    int AccelerationTime { get; set; }

    /// <summary>減速時間 (ms)，對應 P5.020 acc/dec #1</summary>
    int DecelerationTime { get; set; }

    /// <summary>延遲時間 (ms)，對應 P5.040 delay #1</summary>
    int DelayTime { get; set; }

    /// <summary>到位逾時時間 (ms)</summary>
    int InPositionTimeout { get; set; }

    /// <summary>每 mm 對應的 PUU 數量（脈衝/mm）</summary>
    double PuuPerMm { get; set; }

    /// <summary>將運動參數寫入驅動器</summary>
    void ApplySettings();
}