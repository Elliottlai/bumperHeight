using Slot_Inspection.Models;

namespace Slot_Inspection.ViewModels;

/// <summary>
/// QC 基準比對的共用計算邏輯：把目前 SlotItem 結果跟儲存的 QC 基準逐 Slot 相減、依容差判定超差。
/// 供獨立「QC 比對」視窗（<see cref="QcInspectionViewModel"/>）與每輪跑完的自動檢查
/// （<c>MainViewModel.RunQcAutoCheck</c>）共用。
/// </summary>
public static class QcComparisonViewModel
{
    /// <summary>逐 Slot 比對，回傳每個 Slot 的「基準 vs 目前」差異列。</summary>
    public static List<QcSlotComparison> BuildRows(QcBaselineData baseline,
        IEnumerable<SlotItem> currentAreaA, IEnumerable<SlotItem> currentAreaB, InspectionConfig config)
    {
        var result = new List<QcSlotComparison>();
        result.AddRange(Pair("A", baseline.AreaA, currentAreaA.ToList(), config));
        result.AddRange(Pair("B", baseline.AreaB, currentAreaB.ToList(), config));
        return result;
    }

    private static IEnumerable<QcSlotComparison> Pair(
        string area, List<QcBaselineSlot> baselineSlots, List<SlotItem> currentSlots, InspectionConfig config)
    {
        int n = Math.Min(baselineSlots.Count, currentSlots.Count);
        for (int i = 0; i < n; i++)
        {
            var b = baselineSlots[i];
            var c = currentSlots[i];
            double dValue = c.Value - b.Value;
            double dMean = c.MeanIntensity - b.MeanIntensity;
            double dDist = c.DistanceToRoiCenter - b.DistanceToRoiCenter;
            bool exceeds = Math.Abs(dValue) > config.QcToleranceValue
                        || Math.Abs(dMean) > config.QcToleranceMeanIntensity
                        || Math.Abs(dDist) > config.QcToleranceDistanceToRoiCenter;

            yield return new QcSlotComparison
            {
                Area = area,
                Name = c.Name,
                BaselineValue = b.Value,
                CurrentValue = c.Value,
                BaselineMeanIntensity = b.MeanIntensity,
                CurrentMeanIntensity = c.MeanIntensity,
                BaselineDistanceToRoiCenter = b.DistanceToRoiCenter,
                CurrentDistanceToRoiCenter = c.DistanceToRoiCenter,
                ExceedsTolerance = exceeds,
            };
        }
    }
}
