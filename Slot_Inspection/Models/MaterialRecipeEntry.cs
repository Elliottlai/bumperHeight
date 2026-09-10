namespace Slot_Inspection.Models;

/// <summary>
/// 料號設定表的單筆對應：料號 → 側別 → Recipe。
/// 存放於 Config/MaterialRecipeMap.json，由 <see cref="MaterialRecipeManager"/> 管理。
/// </summary>
public sealed class MaterialRecipeEntry
{
    /// <summary>料號（唯一鍵，例如 6101-0014390）。比對時去頭尾空白、不分大小寫。</summary>
    public string MaterialNo { get; set; } = "";

    /// <summary>載台側別，只允許 "R" 或 "L"。</summary>
    public string Side { get; set; } = "R";

    /// <summary>對應的 Recipe 名稱（須為 Config/Recipes 下已存在的 Recipe）。</summary>
    public string RecipeName { get; set; } = "";
}
