namespace Slot_Inspection.Services;

/// <summary>
/// 累計一次 RUN（DryRunAsync）過程中各階段花的時間，結束時輸出一行中文統計摘要，
/// 用於評估總週期時間是否超過客戶規定、找出哪個階段拖慢速度。
/// 只做加總與記錄，不影響、不參與原本的檢測流程邏輯。
/// </summary>
public sealed class RunTimingRecorder
{
    /// <summary>細項預設歸屬的主階段。</summary>
    public const string CaptureParent = "拍照+分析";

    /// <summary>移動類細項的歸屬標籤（Y移動、Z下降、Z上升共用）。</summary>
    public const string MoveParent = "Y移動/Z下降/Z上升";

    private readonly System.Diagnostics.Stopwatch _total = System.Diagnostics.Stopwatch.StartNew();
    private readonly Dictionary<string, (long TotalMs, int Count)> _stages = new();
    // key = (歸屬主階段, 細項名稱)；用 List 保留第一次出現的順序，摘要才會依流程先後排列
    private readonly Dictionary<(string Parent, string Stage), (long TotalMs, int Count)> _details = new();
    private readonly List<(string Parent, string Stage)> _detailOrder = new();

    /// <summary>從建立到目前（或 <see cref="Stop"/> 之後固定）的總耗時毫秒。</summary>
    public long TotalMs => _total.ElapsedMilliseconds;

    /// <summary>總耗時扣掉所有「主階段」後的差額（細項不參與計算，因為已經含在主階段裡）。</summary>
    public long OtherMs => Math.Max(0, TotalMs - _stages.Values.Sum(v => v.TotalMs));

    /// <summary>停止總計時，讓之後的摘要與 CSV 用同一個總耗時。</summary>
    public void Stop() => _total.Stop();

    /// <summary>把某個階段這次花的時間（毫秒）加進累計。同一階段名稱會自動加總、計次。</summary>
    public void Add(string stage, long ms)
    {
        if (_stages.TryGetValue(stage, out var acc))
            _stages[stage] = (acc.TotalMs + ms, acc.Count + 1);
        else
            _stages[stage] = (ms, 1);
    }

    /// <summary>
    /// 記錄某個主階段「內部」的細項（例如拍照+分析裡的取像、AI、補滿延遲）。
    /// 細項只是把主階段拆開來看，不會重複計入「其他」的扣除。
    /// </summary>
    /// <param name="stage">細項名稱。</param>
    /// <param name="ms">這次花的毫秒數。</param>
    /// <param name="parent">這個細項含在哪個主階段裡（摘要分組顯示用）。</param>
    public void AddDetail(string stage, long ms, string parent = CaptureParent)
    {
        var key = (parent, stage);
        if (_details.TryGetValue(key, out var acc))
            _details[key] = (acc.TotalMs + ms, acc.Count + 1);
        else
        {
            _details[key] = (ms, 1);
            _detailOrder.Add(key);
        }
    }

    /// <summary>取某個主階段的累計毫秒，沒有記錄過回傳 0。</summary>
    public long GetStageMs(string stage) => _stages.TryGetValue(stage, out var v) ? v.TotalMs : 0;

    /// <summary>取某個細項的累計毫秒（不分歸屬主階段加總），沒有記錄過回傳 0。</summary>
    public long GetDetailMs(string stage) =>
        _details.Where(kv => kv.Key.Stage == stage).Sum(kv => kv.Value.TotalMs);

    /// <summary>
    /// 組成可直接寫進 log 的中文摘要：總耗時、RUN 前置時間、與基準比較、客戶目標判定、
    /// 每個階段的小計/平均/次數（依耗時排序）、各主階段內部細項，以及「其他」＝總時間扣掉所有主階段後的差額
    /// （含 RUN 前置確認、結束後回原點/回待機位、光閘暫停等待等未被個別計時的部分）。
    /// </summary>
    /// <param name="slotCount">本次 RUN 完成的 Slot 數，0 表示尚未跑完任何一個 Slot 就結束。</param>
    /// <param name="completed">是否完整跑完；中途結束時不做基準比較與客戶目標判定（數字不具比較意義）。</param>
    /// <param name="configNote">選填：附註目前生效的關鍵設定值，方便事後翻 log 時知道這次 RUN 是用什麼設定跑的。</param>
    /// <param name="baselineSecPerSlot">基準每 Slot 秒數（例如優化前實測值，不含 RUN 前置），&lt;=0 不比較。</param>
    /// <param name="targetSec">客戶規定的整批 RUN 秒數（跟「含前置總計」比較），&lt;=0 表示未設定。</param>
    /// <param name="preRunMs">
    /// 進入 DryRunAsync 之前的 RUN 前置時間（讀條碼、MES 查料號等，不含等待人工輸入條碼），
    /// 不在本物件的總計時裡，另外傳入顯示；0 表示沒有量測。
    /// </param>
    public string BuildSummary(int slotCount, bool completed = true, string? configNote = null,
        double baselineSecPerSlot = 0, double targetSec = 0, long preRunMs = 0)
    {
        long totalMs = TotalMs;
        double totalSec = totalMs / 1000.0;
        double preRunSec = preRunMs / 1000.0;
        double totalWithPreSec = totalSec + preRunSec;

        var sb = new System.Text.StringBuilder();
        sb.Append($"[Timing] 本次 RUN 總耗時 {totalSec:F1}s");
        if (slotCount > 0)
            sb.Append($"（共 {slotCount} 個 Slot，平均 {totalSec / slotCount:F2}s/Slot）");
        if (preRunMs > 0)
            sb.Append($"，加上 RUN 前置(讀條碼/MES等) {preRunSec:F1}s → 含前置總計 {totalWithPreSec:F1}s");
        if (!completed)
            sb.Append("（⚠ 本次 RUN 中途結束，以下為結束前的統計，不做基準比較與目標判定）");
        if (!string.IsNullOrWhiteSpace(configNote))
            sb.Append($" ｜ 設定：{configNote}");

        if (completed && slotCount > 0)
        {
            double perSlot = totalSec / slotCount;
            if (baselineSecPerSlot > 0)
            {
                double diff = baselineSecPerSlot - perSlot;
                double pct = -diff / baselineSecPerSlot * 100.0;
                string fasterOrSlower = diff >= 0 ? "快" : "慢";
                sb.Append($" ｜ 與基準比較（不含前置）：基準 {baselineSecPerSlot:F2}s/Slot → 本次 {perSlot:F2}s/Slot，" +
                          $"每 Slot {fasterOrSlower} {Math.Abs(diff):F2}s（{pct:+0.0;-0.0;0.0}%），" +
                          $"整批 {slotCount} Slot 約{fasterOrSlower} {Math.Abs(diff) * slotCount:F1}s");
            }

            if (targetSec > 0)
            {
                double compareSec = preRunMs > 0 ? totalWithPreSec : totalSec;
                string compareLabel = preRunMs > 0 ? "含前置總計" : "總耗時";
                double margin = targetSec - compareSec;
                sb.Append(margin >= 0
                    ? $" ｜ 客戶目標 {targetSec:F1}s vs {compareLabel} {compareSec:F1}s → 未超過（還剩 {margin:F1}s）"
                    : $" ｜ ⚠ 客戶目標 {targetSec:F1}s vs {compareLabel} {compareSec:F1}s → 超過 {-margin:F1}s");
            }
            else
            {
                sb.Append(" ｜ 客戶目標：未設定");
            }
        }

        foreach (var (stage, (ms, count)) in _stages.OrderByDescending(kv => kv.Value.TotalMs))
            sb.Append($" ｜ {stage} 共{ms / 1000.0:F1}s（平均{(count > 0 ? ms / (double)count : 0):F0}ms×{count}次）");

        foreach (var group in _detailOrder.GroupBy(k => k.Parent))
        {
            sb.Append($" ｜ ({group.Key}內) ");
            sb.Append(string.Join("、", group.Select(key =>
            {
                var (ms, count) = _details[key];
                return $"{key.Stage} 共{ms / 1000.0:F1}s（平均{(count > 0 ? ms / (double)count : 0):F0}ms）";
            })));
        }

        sb.Append($" ｜ 其他(前置確認/結束回原點與待機/暫停/未分類) {OtherMs / 1000.0:F1}s");
        return sb.ToString();
    }
}
