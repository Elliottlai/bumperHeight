namespace Slot_Inspection.Models;

/// <summary>一列「基準 vs 目前」比對結果，供 QcComparisonWindow 顯示（純顯示用，不持久化）。</summary>
public sealed class QcSlotComparison
{
    public string Area { get; init; } = "";
    public string Name { get; init; } = "";

    public double BaselineValue { get; init; }
    public double CurrentValue { get; init; }
    public double DeltaValue => CurrentValue - BaselineValue;

    public double BaselineMeanIntensity { get; init; }
    public double CurrentMeanIntensity { get; init; }
    public double DeltaMeanIntensity => CurrentMeanIntensity - BaselineMeanIntensity;

    public double BaselineDistanceToRoiCenter { get; init; }
    public double CurrentDistanceToRoiCenter { get; init; }
    public double DeltaDistanceToRoiCenter => CurrentDistanceToRoiCenter - BaselineDistanceToRoiCenter;

    /// <summary>任一欄位與基準的差距超出對應容許範圍即為 true；驅動 DataGridRow 紅色高亮。</summary>
    public bool ExceedsTolerance { get; init; }

    /// <summary>給畫面直接讀的白話判定文字。</summary>
    public string Verdict => ExceedsTolerance ? "⚠ 偏離基準" : "✔ 正常";
}
