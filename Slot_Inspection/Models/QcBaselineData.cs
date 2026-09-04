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
/// 一份 Recipe「單一載台側別（R 或 L）」的 QC 基準快照，
/// 存於 Config/Recipes/{name}/QcBaseline_{R|L}.json。
/// 由使用者在已知良品板子跑完一輪後，明確存下目前的 SlotItem 結果作為基準。
/// 同一個 Recipe（同料號）的 R 板與 L 板用不同相機/座標拍攝，量測值天生不同，
/// 所以 R、L 各自獨立一份基準檔，比對時依當下偵測到的側別自動取用對應那份。
/// </summary>
public sealed class QcBaselineData
{
    public string RecipeName { get; set; } = "";
    /// <summary>這份基準對應的載台側別："R" 或 "L"。</summary>
    public string CarrierSide { get; set; } = "";
    public DateTime SavedAt { get; set; }
    public string SavedByUsername { get; set; } = "";
    public string SourceBarcode { get; set; } = "";

    public List<QcBaselineSlot> AreaA { get; set; } = [];
    public List<QcBaselineSlot> AreaB { get; set; } = [];

    public static QcBaselineData FromSlotItems(
        string recipeName, string carrierSide, string username, string barcode,
        IEnumerable<SlotItem> areaA, IEnumerable<SlotItem> areaB) => new()
    {
        RecipeName = recipeName,
        CarrierSide = carrierSide,
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
