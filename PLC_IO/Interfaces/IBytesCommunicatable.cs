namespace PLC_IO.Interfaces;

/// <summary>
/// 位元組層級通訊介面
/// </summary>
public interface IBytesCommunicatable
{
    /// <summary>傳送資料</summary>
    void Send(byte[] data);

    /// <summary>取得接收緩衝區資料</summary>
    byte[] Get();

    /// <summary>通訊是否可用</summary>
    bool Communicatable();

    /// <summary>註冊資料到達事件</summary>
    void AddDataArrivalEvent(Action dataArrivalEvent);
}