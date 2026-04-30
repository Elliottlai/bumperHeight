namespace DeltaAxis_RS485.Interfaces;

/// <summary>
/// 絕對型編碼器狀態介面
/// </summary>
public interface IAbsoluteEncoder
{
    /// <summary>檢查 absolute 座標是否正常</summary>
    bool AbsOk();

    /// <summary>重建 absolute origin（座標遺失時使用）</summary>
    void RebuildAbsoluteOrigin();

    /// <summary>讀取目前多圈位置</summary>
    int GetMultiTurnPosition();

    /// <summary>讀取目前單圈位置</summary>
    int GetSingleTurnPosition();
}