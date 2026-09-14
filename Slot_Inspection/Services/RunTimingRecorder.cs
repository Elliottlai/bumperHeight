namespace Slot_Inspection.Services;

/// <summary>
/// 累計一次 RUN（DryRunAsync）過程中各階段花的時間，結束時輸出一行中文統計摘要，
/// 用於評估總週期時間是否超過客戶規定、找出哪個階段拖慢速度。
/// 只做加總與記錄，不影響、不參與原本的檢測流程邏輯。
/// </summary>
public sealed class RunTimingRecorder
{
    private readonly System.Diagnostics.Stopwatch _total = System.Diagnostics.Stopwatch.StartNew();
    private readonly Dictionary<string, (long TotalMs, int Count)> _stages = new();

    /// <summary>把某個階段這次花的時間（毫秒）加進累計。同一階段名稱會自動加總、計次。</summary>
    public void Add(string stage, long ms)
    {
        if (_stages.TryGetValue(stage, out var acc))
            _stages[stage] = (acc.TotalMs + ms, acc.Count + 1);
        else
            _stages[stage] = (ms, 1);
    }

    /// <summary>
    /// 組成可直接寫進 log 的中文摘要：總耗時、每個階段的小計/平均/次數（依耗時排序），
    /// 以及「其他」＝總時間扣掉所有已分類階段後的差額（含光閘暫停等待、EnsureZSafe 內部
    /// 額外動作等未被個別計時的部分）。
    /// </summary>
    public string BuildSummary(int slotCount)
    {
        long totalMs = _total.ElapsedMilliseconds;
        long accountedMs = _stages.Values.Sum(v => v.TotalMs);

        var sb = new System.Text.StringBuilder();
        sb.Append($"[Timing] 本次 RUN 總耗時 {totalMs / 1000.0:F1}s");
        if (slotCount > 0)
            sb.Append($"（共 {slotCount} 個 Slot，平均 {totalMs / (double)slotCount / 1000.0:F2}s/Slot）");

        foreach (var (stage, (ms, count)) in _stages.OrderByDescending(kv => kv.Value.TotalMs))
            sb.Append($" ｜ {stage} 共{ms / 1000.0:F1}s（平均{(count > 0 ? ms / (double)count : 0):F0}ms×{count}次）");

        sb.Append($" ｜ 其他(等待/暫停/未分類) {Math.Max(0, totalMs - accountedMs) / 1000.0:F1}s");
        return sb.ToString();
    }
}
