namespace PLC_IO.Interfaces;

/// <summary>
/// PLC 通訊器介面 — 抽象化 FX 系列 PLC 的讀寫操作
/// </summary>
public interface IPlcCommunicator : IDisposable
{
    /// <summary>是否已連線</summary>
    bool IsConnected { get; }

    /// <summary>看門狗計數值（通訊活動指標）</summary>
    long DogValue { get; }

    /// <summary>輪詢間隔（毫秒）</summary>
    int RefreshInterval { get; set; }

    /// <summary>讀取 X 輸入點狀態</summary>
    bool GetX(int index);

    /// <summary>讀取 Y 輸出點狀態</summary>
    bool GetY(int index);

    /// <summary>設定 Y 輸出點狀態</summary>
    void SetY(int index, bool value);
}