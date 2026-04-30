namespace DeltaAxis_RS485.Interfaces;

/// <summary>
/// 伺服驅動器控制介面
/// </summary>
public interface IServoDriver
{
    /// <summary>建立通訊連線</summary>
    void Connect();

    /// <summary>伺服激磁 (Servo ON)</summary>
    void ServoOn();

    /// <summary>伺服解磁 (Servo OFF)</summary>
    void ServoOff();

    /// <summary>檢查是否有警報</summary>
    bool HasAlarm();

    /// <summary>清除警報</summary>
    void ClearAlarm();

    /// <summary>是否已激磁</summary>
    bool IsServoOn { get; }
}