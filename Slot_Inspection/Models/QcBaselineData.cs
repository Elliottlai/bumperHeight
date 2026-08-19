namespace Slot_Inspection.Models;

/// <summary>單一 Slot 的 QC 基準快照。</summary>
public sealed class QcBaselineSlot
{
    public string Name { get; set; } = "";
    public double Value { get; set; }
    public double MeanIntensity { get; set; }
    public double DistanceToRoiCenter { get; set; }
}

/// <summary>
/// 一份 Recipe 的 QC 基準快照，存於 Config/Recipes/{name}/QcBaseline.json。
/// 由使用者在已知良品板子跑完一輪後，明確存下目前的 SlotItem 結果作為基準。
/// </summary>
public sealed class QcBaselineData
{
    public string RecipeName { get; set; } = "";
    public DateTime SavedAt { get; set; }
    public string SavedByUsername { get; set; } = "";
    public string SourceBarcode { get; set; } = "";

    public List<QcBaselineSlot> AreaA { get; set; } = [];
    public List<QcBaselineSlot> AreaB { get; set; } = [];

    public static QcBaselineData FromSlotItems(
        string recipeName, string username, string barcode,
        IEnumerable<SlotItem> areaA, IEnumerable<SlotItem> areaB) => new()
    {
        RecipeName = recipeName,
        SavedAt = DateTime.Now,
        SavedByUsername = username,
        SourceBarcode = barcode,
        AreaA = areaA.Select(ToSnapshot).ToList(),
        AreaB = areaB.Select(ToSnapshot).ToList(),
    };

    private static QcBaselineSlot ToSnapshot(SlotItem s) => new()
    {
        Name = s.Name,
        Value = s.Value,
        MeanIntensity = s.MeanIntensity,
        DistanceToRoiCenter = s.DistanceToRoiCenter,
    };
}
