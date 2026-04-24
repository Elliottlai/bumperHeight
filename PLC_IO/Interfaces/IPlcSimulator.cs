namespace PLC_IO.Interfaces;

/// <summary>
/// PLC 模擬器介面 — 用於 AsPLC 模式（模擬 PLC 端）
/// </summary>
public interface IPlcSimulator
{
    /// <summary>設定模擬 X 輸入點狀態</summary>
    void SetX(int index, bool value);
}