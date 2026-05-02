using DeltaAxis_RS485.Models;

namespace DeltaAxis_RS485.Interfaces;

/// <summary>
/// 絕對型編碼器狀態介面
/// </summary>
public interface IAbsoluteEncoder
{
    /// <summary>檢查 absolute 座標是否全部正常 (P0.050 = 0)</summary>
    bool AbsOk();

    /// <summary>讀取 P0.050 完整狀態旗標</summary>
    AbsoluteStatusFlags ReadAbsoluteStatus();

    /// <summary>檢查絕對位置是否正常 (P0.050 Bit0)</summary>
    bool IsAbsolutePositionOk();

    /// <summary>檢查電池電壓是否正常 (P0.050 Bit1)</summary>
    bool IsBatteryVoltageOk();

    /// <summary>檢查絕對圈數是否正常 (P0.050 Bit2)</summary>
    bool IsAbsoluteRevolutionOk();

    /// <summary>檢查 PUU 狀態是否正常 (P0.050 Bit3)</summary>
    bool IsPuuStatusOk();

    /// <summary>檢查絕對座標是否正常 (P0.050 Bit4)</summary>
    bool IsAbsoluteCoordinateOk();

    /// <summary>重建 absolute origin（座標遺失時使用）</summary>
    void RebuildAbsoluteOrigin();

    /// <summary>讀取目前多圈位置</summary>
    int GetMultiTurnPosition();

    /// <summary>讀取目前單圈位置</summary>
    int GetSingleTurnPosition();
}