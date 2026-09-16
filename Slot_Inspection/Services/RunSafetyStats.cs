namespace Slot_Inspection.Services;

/// <summary>
/// 累計一次 RUN（DryRunAsync）過程中的安全相關證據，結束時輸出一行 [Safety] 摘要，
/// 用來證明「週期時間優化之後，每次 Y 移動前仍然有真的確認 Z 在安全高度」，
/// 以及軸位置讀值是否可信（讀值不可信時，Z 安全檢查等於沒作用）。
/// 只做統計與記錄，不影響、不參與任何流程判斷。
/// </summary>
public sealed class RunSafetyStats
{
    /// <summary>判定「讀回位置可疑」的偏差門檻（mm）。</summary>
    public const double ReadbackSuspectThresholdMm = 1.0;

    private readonly Dictionary<string, double> _maxDeviation = new();
    private readonly HashSet<string> _readbackWarnedAxes = new();

    /// <summary>Y 移動前 Z 安全確認通過的次數。</summary>
    public int ZSafeCheckCount { get; private set; }

    /// <summary>所有 Y 移動前確認中，ZL 距安全上限的最小餘裕（安全上限 − 實際位置，mm；正值 = 比安全上限還高）。</summary>
    public double MinMarginZL { get; private set; } = double.PositiveInfinity;

    /// <summary>同 <see cref="MinMarginZL"/>，ZR 軸。</summary>
    public double MinMarginZR { get; private set; } = double.PositiveInfinity;

    /// <summary>本次 RUN 是否曾偵測到讀回位置可疑。</summary>
    public bool ReadbackSuspect { get; private set; }

    /// <summary>記錄一次「Y 移動前 Z 安全確認」通過當下的 ZL/ZR 實際位置與安全上限。</summary>
    public void RecordZSafeCheck(double zl, double zr, double safeZL, double safeZR)
    {
        ZSafeCheckCount++;
        MinMarginZL = Math.Min(MinMarginZL, safeZL - zl);
        MinMarginZR = Math.Min(MinMarginZR, safeZR - zr);
    }

    /// <summary>
    /// 記錄一次軸到位後「目標 vs 讀回實際位置」的偏差。
    /// 若這次移動距離明顯（|目標 − 移動前位置| &gt; 門檻）但讀回位置離目標也超過門檻，
    /// 代表讀回位置很可能不可信（例如一直讀到 0），回傳一段警告文字（每軸每次 RUN 只回傳一次），否則回傳 null。
    /// </summary>
    /// <param name="axisName">軸名稱，例如 "AxisZL"。</param>
    /// <param name="target">這次移動的目標位置（mm）。</param>
    /// <param name="actual">到位後讀回的實際位置（mm）。</param>
    /// <param name="moveFrom">移動前的預期位置（mm）；傳 <see cref="double.NaN"/> 表示不做可疑判斷，只記偏差。</param>
    public string? RecordArrival(string axisName, double target, double actual, double moveFrom = double.NaN)
    {
        double deviation = Math.Abs(actual - target);
        _maxDeviation[axisName] = _maxDeviation.TryGetValue(axisName, out var prev) ? Math.Max(prev, deviation) : deviation;

        bool movedEnough = !double.IsNaN(moveFrom) && Math.Abs(target - moveFrom) > ReadbackSuspectThresholdMm;
        if (movedEnough && deviation > ReadbackSuspectThresholdMm)
        {
            ReadbackSuspect = true;
            if (_readbackWarnedAxes.Add(axisName))
                return $"[AxisReadback] ⚠ {axisName} 讀回位置異常（從 {moveFrom:F2} mm 移到目標 {target:F2} mm，" +
                       $"到位後讀回 {actual:F2} mm，偏差 {deviation:F2} mm），Z 安全高度檢查可能失效，請確認軸位置讀值與換算比例（Scale）";
        }
        return null;
    }

    /// <summary>取某軸本次 RUN 的最大到位偏差（mm），沒有記錄過回傳 NaN。</summary>
    public double GetMaxDeviation(string axisName) =>
        _maxDeviation.TryGetValue(axisName, out var v) ? v : double.NaN;

    /// <summary>組成可直接寫進 log 的 [Safety] 摘要。</summary>
    /// <param name="plannedYMoves">本次 RUN 預期要做的 Slot 間 Y 移動次數（= 完成的 Slot 數）。</param>
    public string BuildSummary(int plannedYMoves)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append($"[Safety] 本次 RUN：Y 移動前 Z 安全確認通過 {ZSafeCheckCount} 次（完成 Slot {plannedYMoves} 個）");
        if (ZSafeCheckCount > 0)
            sb.Append($"，距安全上限最小餘裕 ZL {MinMarginZL:+0.00;-0.00;0.00} mm / ZR {MinMarginZR:+0.00;-0.00;0.00} mm" +
                      "（正值 = 比安全上限還高；負值在容差內仍算通過）");

        var devParts = new[] { "AxisY", "AxisZL", "AxisZR" }
            .Where(_maxDeviation.ContainsKey)
            .Select(a => $"{a.Replace("Axis", "")} {_maxDeviation[a]:F2}mm")
            .ToList();
        if (devParts.Count > 0)
            sb.Append($" ｜ 到位最大偏差 {string.Join(" / ", devParts)}");

        sb.Append(ReadbackStatus switch
        {
            "異常" => " ｜ ⚠ 位置讀值自我檢查：異常（見上方 [AxisReadback] 警告）",
            "未檢查" => " ｜ 位置讀值自我檢查：未檢查（沒有完成任何軸移動）",
            _ => " ｜ 位置讀值自我檢查：正常",
        });
        return sb.ToString();
    }

    /// <summary>位置讀值自我檢查結果：「異常」/「未檢查」（沒有完成任何軸移動）/「正常」。</summary>
    public string ReadbackStatus =>
        ReadbackSuspect ? "異常" : _maxDeviation.Count == 0 ? "未檢查" : "正常";
}
