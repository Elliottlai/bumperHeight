namespace PLC_IO.Interfaces;

/// <summary>
/// 命令處理介面 — 用於請求/回覆流程控制
/// </summary>
public interface ICommandHandler<T>
{
    /// <summary>發送命令</summary>
    void SendCommand(T command);

    /// <summary>檢查命令是否已收到回覆</summary>
    bool CheckIsCommandAnswered(T command);

    /// <summary>空閒時處理（如週期性讀取）</summary>
    void IdleProcess();
}