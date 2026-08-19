using System.Collections.ObjectModel;
using Slot_Inspection.Models;

namespace Slot_Inspection.ViewModels;

/// <summary>QC 基準比對視窗的 ViewModel：把目前 SlotItem 結果跟儲存的 QC 基準逐 Slot 比對。</summary>
public sealed class QcComparisonViewModel : ObservableObject
{
    public string RecipeName { get; }
    public DateTime BaselineSavedAt { get; }
    public string BaselineSavedBy { get; }
    public ObservableCollection<QcSlotComparison> Rows { get; } = [];
    public int ExceedCount => Rows.Count(r => r.ExceedsTolerance);

    public QcComparisonViewModel(QcBaselineData baseline,
        IEnumerable<SlotItem> currentAreaA, IEnumerable<SlotItem> currentAreaB, InspectionConfig config)
    {
        RecipeName = baseline.RecipeName;
        BaselineSavedAt = baseline.SavedAt;
        BaselineSavedBy = baseline.SavedByUsername;
        foreach (var row in BuildRows(baseline, currentAreaA, currentAreaB, config))
            Rows.Add(row);
    }

    /// <summary>共用比對邏輯，供視窗與 MainViewModel 跑完一輪後的自動檢查共用。</summary>
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
