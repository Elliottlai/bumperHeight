using System.Collections.Concurrent;
using NLog;

namespace Slot_Inspection.Services;

/// <summary>單一筆已觸發的異常原因。</summary>
public sealed class AlarmEntry
{
    /// <summary>異常原因代碼，例如 "LightGateX7"、"RunException"。同一代碼重複觸發視為同一筆，不會疊加。</summary>
    public required string Code { get; init; }

    /// <summary>給人看的說明文字，顯示在狀態列/異常清單。</summary>
    public required string Message { get; init; }

    public DateTime RaisedAt { get; init; }
}

/// <summary>
/// 異常燈號的單一事實來源。
///
/// 設計動機：舊寫法是在 MainViewModel 四個不同地方各自用一個裸 bool（IsAlarmActive）
/// 搭配 MachineController.SetRedAlarm(true/false) 直接寫 PLC 線圈，沒有任何地方記錄「目前
/// 到底是哪個原因造成異常」，導致：
///   1. 任何一處呼叫 SetRedAlarm(false) 都會把燈熄掉，即使其他原因仍然成立；
///   2. 沒有人知道現在紅燈亮著到底是為什麼，只能回頭翻 log；
///   3. PLC 線圈是實體輸出，程式關閉不會重置，若「熄燈」那次寫入剛好因為 PLC 未連線而被
///      MachineController.SetPlcYOctal 悄悄丟棄（見該方法內 `!_plc.IsConnected` 直接 return），
///      物理紅燈會一路維持上次的狀態到下次開機，且不會有任何重試。
///
/// 這個類別只負責維護「目前有哪些具名異常原因」這件事本身，不碰 PLC、不碰 UI。
/// 實際寫入 PLC 的部分由 MachineController.SyncRedAlarmOutput() 負責，且必須用「每個 tick
/// 重新斷言目前狀態」的方式呼叫（而不是只在 Raise/Clear 當下呼叫一次），這樣即使某次寫入
/// 因為 PLC 斷線而失效，下一個 tick（PLC 重新連線後）就會自動補上正確狀態，不需要使用者
/// 手動按任何按鈕才能讓燈熄滅。
/// </summary>
public sealed class AlarmManager
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

    private readonly ConcurrentDictionary<string, AlarmEntry> _active = new();

    /// <summary>異常集合有任何變動（新增或移除任一原因）時觸發。不帶資料，讀者自行呼叫 GetActive()。</summary>
    public event Action? StateChanged;

    /// <summary>目前是否有任一異常原因存在中——紅燈該不該亮的唯一判斷依據。</summary>
    public bool IsActive => !_active.IsEmpty;

    /// <summary>目前所有生效中的異常原因，依觸發時間排序，供 UI 顯示清單用。</summary>
    public IReadOnlyList<AlarmEntry> GetActive() => _active.Values.OrderBy(a => a.RaisedAt).ToList();

    /// <summary>
    /// 標記某個具名原因為異常中。同一 code 重複呼叫只更新訊息/時間，不會重複記 log 或重複觸發事件，
    /// 避免同一故障持續中時每個 tick 都洗版。
    /// </summary>
    public void Raise(string code, string message)
    {
        bool isNew = !_active.ContainsKey(code);
        _active[code] = new AlarmEntry { Code = code, Message = message, RaisedAt = DateTime.Now };
        if (isNew)
        {
            _logger.Warn($"[Alarm] 觸發 {code}：{message}");
            StateChanged?.Invoke();
        }
    }

    /// <summary>
    /// 清除某個具名原因。呼叫端有責任確保該原因對應的實體條件已經不成立
    /// （例如軸警報要先確認 axis.GetAlarm() 已經是 false 才能呼叫，不可無條件清除）。
    /// </summary>
    public void Clear(string code)
    {
        if (_active.TryRemove(code, out var entry))
        {
            _logger.Info($"[Alarm] 解除 {code}（原因：{entry.Message}）");
            StateChanged?.Invoke();
        }
    }
}
