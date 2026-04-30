namespace DeltaAxis_RS485.Models;

/// <summary>
/// 伺服警報例外
/// </summary>
public class ServoAlarmException : Exception
{
    /// <summary>警報代碼</summary>
    public ushort AlarmCode { get; }

    public ServoAlarmException(ushort alarmCode)
        : base($"伺服驅動器發生警報，代碼: 0x{alarmCode:X4}")
    {
        AlarmCode = alarmCode;
    }

    public ServoAlarmException(ushort alarmCode, string message)
        : base(message)
    {
        AlarmCode = alarmCode;
    }
}