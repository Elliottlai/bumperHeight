namespace DeltaAxis_RS485.Models;

/// <summary>
/// RS485 通訊連線設定
/// </summary>
public class ConnectionSettings
{
    /// <summary>COM Port 名稱，例如 "COM3"</summary>
    public string PortName { get; set; } = "COM3";

    /// <summary>鮑率，預設 115200</summary>
    public int BaudRate { get; set; } = 115200;

    /// <summary>Modbus 站號，預設 1</summary>
    public byte SlaveId { get; set; } = 1;
}