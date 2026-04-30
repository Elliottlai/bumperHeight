namespace DeltaAxis_RS485.Models;

/// <summary>
/// 單軸完整設定（1 COM Port = 1 軸）
/// </summary>
public class AxisConfig
{
    public string Name { get; set; } = "Axis";
    public string PortName { get; set; } = "COM3";
    public int BaudRate { get; set; } = 115200;
    public byte SlaveId { get; set; } = 1;
    public MotionSettings Motion { get; set; } = new();
}