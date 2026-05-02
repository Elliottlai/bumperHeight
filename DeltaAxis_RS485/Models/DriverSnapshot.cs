namespace DeltaAxis_RS485.Models;

/// <summary>
/// 驅動器即時狀態快照（批次讀取結果）
/// </summary>
public readonly record struct DriverSnapshot
{
    /// <summary>DI 狀態 (P0.045)</summary>
    public ushort DiStatus { get; init; }

    /// <summary>驅動器狀態旗標 (P0.046)</summary>
    public DriverStatusFlags DriverStatus { get; init; }

    /// <summary>絕對座標狀態 (P0.050)</summary>
    public AbsoluteStatusFlags AbsoluteStatus { get; init; }

    /// <summary>多圈位置 PUU (P0.051, 32-bit)</summary>
    public int MultiTurnPosition { get; init; }

    /// <summary>單圈位置 PUU (P0.052, 32-bit)</summary>
    public int SingleTurnPosition { get; init; }

    // ============================
    //  監視變數映射 (P0.009~P0.012)
    // ============================

    /// <summary>回授位置 PUU (P0.009, 對應 MonitorCode 0x00)</summary>
    public int FeedbackPositionPuu { get; init; }

    /// <summary>警報碼十進位 (P0.010, 對應 MonitorCode 0x1C)</summary>
    public ushort AlarmCodeDecimal { get; init; }

    /// <summary>DI 整合狀態 (P0.011, 對應 MonitorCode 0x27)</summary>
    public ushort DiStatusIntegrated { get; init; }

    /// <summary>DO 硬體狀態 (P0.012, 對應 MonitorCode 0x28)</summary>
    public ushort DoStatusHardware { get; init; }

    // 衍生屬性
    public bool HasAlarm => DriverStatus.HasFlag(DriverStatusFlags.Alarm);
    public bool IsServoReady => DriverStatus.HasFlag(DriverStatusFlags.ServoReady);
    public bool IsServoOn => DriverStatus.HasFlag(DriverStatusFlags.ServoOn);
    public bool IsInPosition => DriverStatus.HasFlag(DriverStatusFlags.TargetPositionReached);
    public bool IsHomeComplete => DriverStatus.HasFlag(DriverStatusFlags.HomeComplete);
    public bool IsBrakeReleased => DriverStatus.HasFlag(DriverStatusFlags.BrakeRelease);
    public bool AbsOk => AbsoluteStatus == AbsoluteStatusFlags.None;
}