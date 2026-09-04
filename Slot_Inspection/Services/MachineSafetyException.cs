namespace Slot_Inspection.Services;

/// <summary>
/// 機構安全閘攔截：在不安全狀態下嘗試發出「會造成碰撞」的軸移動指令時拋出。
/// 跟 <see cref="System.OperationCanceledException"/>（使用者主動取消）刻意分開，
/// 方便呼叫端 / log 判讀「這是被安全機制擋下，不是使用者取消」。
/// </summary>
public sealed class MachineSafetyException : System.Exception
{
    public MachineSafetyException(string message) : base(message) { }
}
