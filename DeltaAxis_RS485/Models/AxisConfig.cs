namespace DeltaAxis_RS485.Models;

/// <summary>
/// 單軸完整設定（1 COM Port = 1 軸）
/// </summary>
public class AxisConfig
{
    public string Name { get; set; } = "Axis";
    public string PortName { get; set; } = "COM3";
    public int BaudRate { get; set; } = 38400;
    public byte SlaveId { get; set; } = 1;
    public MotionSettings Motion { get; set; } = new();

    /// <summary>
    /// 「+」方向的物理意義說明（例如「下降」），給FAE在Jog時看得懂實際方向，避免誤判方向撞機。
    /// 留空則Jog按鈕只顯示數學正負號（例如「+10」），不額外標示方向。
    /// </summary>
    public string PositiveDirectionLabel { get; set; } = "";

    /// <summary>「-」方向的物理意義說明（例如「上升」）。留空則不額外標示方向。</summary>
    public string NegativeDirectionLabel { get; set; } = "";

    /// <summary>
    /// 這顆軸上已知需要留意的參考位置（例如Y軸的取料點）。
    /// Jog/移動目標位置落在參考點 ± WarnToleranceMm 範圍內時，會跳出警告但仍可選擇繼續執行。
    /// </summary>
    public List<AxisReferencePosition> ReferencePositions { get; set; } = [];

    /// <summary>
    /// Jog按鈕排列方式：false（預設）＝負/停止/正全部擠在一排（自動換行）；
    /// true＝負方向、停止、正方向各自獨立一排顯示，給方向標籤較長的軸（例如Y/X軸的「靠近/遠離原點」）使用。
    /// </summary>
    public bool JogButtonsMultiRow { get; set; } = false;
}

/// <summary>一個已知參考位置（例如取料點），用於Jog/移動前的接近警告。</summary>
public class AxisReferencePosition
{
    public string Name { get; set; } = "";
    public double PositionMm { get; set; }

    /// <summary>容許誤差範圍（mm）：目標位置跟這個參考點的距離在此範圍內就會警告。</summary>
    public double WarnToleranceMm { get; set; } = 5.0;
}
