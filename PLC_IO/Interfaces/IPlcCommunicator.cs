namespace PLC_IO.Interfaces;

/// <summary>
/// PLC �q�T������ �X ��H�� FX �t�C PLC ��Ū�g�ާ@
/// </summary>
public interface IPlcCommunicator : IDisposable
{
    /// <summary>�O�_�w�s�u</summary>
    bool IsConnected { get; }

    /// <summary>
    /// X 輸入點快取資料是否「新鮮」（最近一段時間內有成功解析過新的 X 訊框）。
    /// IsConnected 只代表底層連線（例如 COM Port）還開著，不代表讀取真的還在正常更新——
    /// 如果連線沒斷但讀取持續失敗/被卡住，GetX() 只會不斷回傳同一筆舊的快取值。
    /// 呼叫端（尤其是光閘 X7 這類安全訊號）應該同時檢查 IsConnected 跟這個屬性，
    /// 兩者都成立才能信任 GetX() 的回傳值是「現在」的狀態，否則應視同斷線處理。
    /// </summary>
    bool IsXDataFresh { get; }

    /// <summary>�ݪ����p�ƭȡ]�q�T���ʫ��С^</summary>
    long DogValue { get; }

    /// <summary>���߶��j�]�@���^</summary>
    int RefreshInterval { get; set; }

    /// <summary>Ū�� X ��J�I���A</summary>
    bool GetX(int index);

    /// <summary>Ū�� Y ��X�I���A</summary>
    bool GetY(int index);

    /// <summary>�]�w Y ��X�I���A</summary>
    void SetY(int index, bool value);
}