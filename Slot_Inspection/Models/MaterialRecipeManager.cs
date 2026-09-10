namespace Slot_Inspection.Models;

using System.IO;
using System.Text.Json;

/// <summary>
/// 料號設定表（料號 → 側別 → Recipe）的存讀管理，存放於 Config/MaterialRecipeMap.json。
/// 後續「掃碼自動切換 Recipe」功能會用 <see cref="Find"/> 依 MES 回傳的料號查出應套用的
/// Recipe / 側別；本階段僅提供編輯與持久化。設定檔損毀時一律回傳空清單，絕不讓程式崩潰。
/// </summary>
public static class MaterialRecipeManager
{
    private static readonly string FilePath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "Config", "MaterialRecipeMap.json");

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    /// <summary>讀取整張表；檔案不存在或解析失敗回傳空清單。</summary>
    public static List<MaterialRecipeEntry> LoadAll()
    {
        if (!File.Exists(FilePath)) return new List<MaterialRecipeEntry>();
        try
        {
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<List<MaterialRecipeEntry>>(json, JsonOpts)
                   ?? new List<MaterialRecipeEntry>();
        }
        catch
        {
            return new List<MaterialRecipeEntry>();
        }
    }

    private static void SaveAll(List<MaterialRecipeEntry> list)
    {
        list.Sort((a, b) => string.Compare(a.MaterialNo, b.MaterialNo, StringComparison.OrdinalIgnoreCase));
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(list, JsonOpts));
    }

    /// <summary>正規化料號：去頭尾空白。</summary>
    private static string Norm(string? materialNo) => (materialNo ?? "").Trim();

    private static bool KeyEquals(string? a, string? b)
        => string.Equals(Norm(a), Norm(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>料號是否已存在（去頭尾空白、不分大小寫）。</summary>
    public static bool Exists(string materialNo)
    {
        var key = Norm(materialNo);
        return key.Length > 0 && LoadAll().Any(e => KeyEquals(e.MaterialNo, key));
    }

    /// <summary>
    /// 依料號查出對應設定（去頭尾空白、不分大小寫比對）；找不到回傳 null。
    /// 後續功能會用這個方法把 MES 料號轉成要套用的 Recipe / 側別；本階段尚未使用。
    /// </summary>
    public static MaterialRecipeEntry? Find(string materialNo)
    {
        var key = Norm(materialNo);
        if (key.Length == 0) return null;
        return LoadAll().FirstOrDefault(e => KeyEquals(e.MaterialNo, key));
    }

    /// <summary>
    /// 新增一筆；料號空白或已存在時回傳 false。
    /// 側別 / Recipe 的有效性由呼叫端負責驗證，這裡只負責唯一鍵與持久化。
    /// </summary>
    public static bool Add(string materialNo, string side, string recipeName)
    {
        var key = Norm(materialNo);
        if (key.Length == 0) return false;
        var list = LoadAll();
        if (list.Any(e => KeyEquals(e.MaterialNo, key))) return false;
        list.Add(new MaterialRecipeEntry { MaterialNo = key, Side = side, RecipeName = recipeName });
        SaveAll(list);
        return true;
    }

    /// <summary>更新既有料號的 側別 / Recipe；找不到該料號時回傳 false。</summary>
    public static bool Update(string materialNo, string side, string recipeName)
    {
        var key = Norm(materialNo);
        var list = LoadAll();
        var entry = list.FirstOrDefault(e => KeyEquals(e.MaterialNo, key));
        if (entry == null) return false;
        entry.Side = side;
        entry.RecipeName = recipeName;
        SaveAll(list);
        return true;
    }

    /// <summary>刪除指定料號；不存在時回傳 false。</summary>
    public static bool Delete(string materialNo)
    {
        var key = Norm(materialNo);
        var list = LoadAll();
        int removed = list.RemoveAll(e => KeyEquals(e.MaterialNo, key));
        if (removed == 0) return false;
        SaveAll(list);
        return true;
    }
}
