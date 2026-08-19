namespace Slot_Inspection.Models;

using System.IO;
using System.Text.Json;

/// <summary>
/// Y/ZL/ZR 軸參數組合（Recipe）的存讀管理，存放於 Config/Recipes/{name}.json。
/// 與即時生效設定（SlotPositionTable / InspectionConfig）分離，讀取/建立/刪除
/// 只操作 Recipe 檔案本身，不會直接影響機台正在使用的座標。
/// </summary>
public static class AxisRecipeManager
{
    private static readonly string RecipesDir = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "Config", "Recipes");

    private static readonly string ConfigDir = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "Config");

    // Config 根目錄下這些是「設定檔」不是「演算法參數檔」，複製演算法JSON時要排除
    private static readonly string[] NonAlgFileNames =
        ["InspectionConfig.json", "SlotPositions.json", "CimApiSettings.json"];

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private static string FilePath(string name) => Path.Combine(RecipesDir, name + ".json");

    private static string AlgFolderPath(string name) => Path.Combine(RecipesDir, name);

    /// <summary>檢查名稱非空白且不含檔名不允許的字元。</summary>
    public static bool IsValidName(string name) =>
        !string.IsNullOrWhiteSpace(name) &&
        name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    /// <summary>列出所有已存在的 Recipe 名稱（不含副檔名），依字母排序。</summary>
    public static string[] List()
    {
        if (!Directory.Exists(RecipesDir)) return [];
        return Directory.GetFiles(RecipesDir, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => !string.IsNullOrEmpty(n))
            .Select(n => n!)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static bool Exists(string name) => File.Exists(FilePath(name));

    /// <summary>儲存（或覆寫）一組 Recipe。</summary>
    public static void Save(string name, AxisRecipeData data)
    {
        Directory.CreateDirectory(RecipesDir);
        File.WriteAllText(FilePath(name), JsonSerializer.Serialize(data, JsonOpts));
    }

    /// <summary>讀取一組 Recipe；找不到檔案或解析失敗時丟例外，由呼叫端顯示錯誤訊息。</summary>
    public static AxisRecipeData Load(string name)
    {
        string path = FilePath(name);
        if (!File.Exists(path))
            throw new FileNotFoundException($"找不到 Recipe 檔案：{path}");

        var data = JsonSerializer.Deserialize<AxisRecipeData>(File.ReadAllText(path), JsonOpts);
        return data ?? throw new InvalidDataException($"Recipe 檔案內容無法解析：{path}");
    }

    /// <summary>刪除一組 Recipe。檔案不存在時不做任何事。</summary>
    public static void Delete(string name)
    {
        string path = FilePath(name);
        if (File.Exists(path))
            File.Delete(path);
    }

    /// <summary>把 Config 根目錄現有的演算法 JSON（Parameter.json + 各相機Slot檔）複製進 Recipe 專屬資料夾。回傳實際備份的檔案數，供呼叫端顯示給使用者確認。</summary>
    public static int SaveAlgFiles(string name)
    {
        string dir = AlgFolderPath(name);
        Directory.CreateDirectory(dir);
        int count = 0;
        foreach (var file in Directory.GetFiles(ConfigDir, "*.json"))
        {
            string fileName = Path.GetFileName(file);
            if (NonAlgFileNames.Contains(fileName, StringComparer.OrdinalIgnoreCase)) continue;
            File.Copy(file, Path.Combine(dir, fileName), overwrite: true);
            count++;
        }
        return count;
    }

    /// <summary>
    /// 把 Recipe 專屬資料夾裡的演算法 JSON 複製回 Config 根目錄（覆蓋）。
    /// 沒有該資料夾（舊Recipe無演算法快照）則略過並回傳 false。
    /// </summary>
    public static bool LoadAlgFiles(string name)
    {
        string dir = AlgFolderPath(name);
        if (!Directory.Exists(dir)) return false;
        foreach (var file in Directory.GetFiles(dir, "*.json"))
            File.Copy(file, Path.Combine(ConfigDir, Path.GetFileName(file)), overwrite: true);
        return true;
    }

    /// <summary>刪除 Recipe 專屬的演算法JSON資料夾。資料夾不存在時不做任何事。</summary>
    public static void DeleteAlgFiles(string name)
    {
        string dir = AlgFolderPath(name);
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
    }

    private static string QcBaselinePath(string name) => Path.Combine(AlgFolderPath(name), "QcBaseline.json");

    /// <summary>是否已有這個 Recipe 的 QC 基準快照。</summary>
    public static bool HasBaseline(string name) => File.Exists(QcBaselinePath(name));

    /// <summary>儲存（或覆寫）一份 Recipe 的 QC 基準快照。</summary>
    public static void SaveBaseline(string name, QcBaselineData data)
    {
        string dir = AlgFolderPath(name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(QcBaselinePath(name), JsonSerializer.Serialize(data, JsonOpts));
    }

    /// <summary>讀取一份 Recipe 的 QC 基準快照；找不到檔案或解析失敗時丟例外，由呼叫端顯示錯誤訊息。</summary>
    public static QcBaselineData LoadBaseline(string name)
    {
        string path = QcBaselinePath(name);
        if (!File.Exists(path))
            throw new FileNotFoundException($"找不到 QC 基準檔案：{path}");

        var data = JsonSerializer.Deserialize<QcBaselineData>(File.ReadAllText(path), JsonOpts);
        return data ?? throw new InvalidDataException($"QC 基準檔案內容無法解析：{path}");
    }
}
