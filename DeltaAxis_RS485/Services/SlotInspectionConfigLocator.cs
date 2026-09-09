using System.IO;

namespace DeltaAxis_RS485.Services;

/// <summary>
/// 負責「找出 Slot_Inspection 主程式的 Config 資料夾在哪」。
/// 依優先順序試一串候選位置，第一個「資料夾存在，且裡面同時有 InspectionConfig.json 與 SlotPositions.json」的就採用。
/// 相對路徑一律以「本 exe 所在資料夾」（AppContext.BaseDirectory）為基準展開，
/// 不受程式啟動時工作目錄影響。
/// </summary>
public static class SlotInspectionConfigLocator
{
    /// <summary>臨時指定用的環境變數名稱（設了就優先採用，不用改程式）。</summary>
    public const string EnvVarName = "SLOT_INSPECTION_CONFIG";

    /// <summary>機台（實機）固定的網路芳鄰共用路徑。</summary>
    public const string MachineSharePath =
        @"\\DESKTOP-0CCMP1C\Software\Slot_Inspection_正式版\net8.0-windows\Config";

    /// <summary>
    /// 依優先順序回傳所有候選資料夾（不保證存在，呼叫端負責檢查）。
    /// </summary>
    /// <param name="uiHint">使用者這次在 UI 輸入框填的路徑（可為空）。</param>
    /// <param name="lastGood">DeltaAxisSettings.json 記住的上次成功路徑（可為空）。</param>
    public static IEnumerable<string> CandidateFolders(string? uiHint, string? lastGood)
    {
        var baseDir = AppContext.BaseDirectory;

        // 1. 使用者這次在 UI 指定的
        if (!string.IsNullOrWhiteSpace(uiHint))
            yield return ToAbsolute(uiHint, baseDir);

        // 2. 環境變數覆寫
        var env = Environment.GetEnvironmentVariable(EnvVarName);
        if (!string.IsNullOrWhiteSpace(env))
            yield return ToAbsolute(env, baseDir);

        // 3. 上次成功讀到、記在 DeltaAxisSettings.json 裡的
        if (!string.IsNullOrWhiteSpace(lastGood))
            yield return ToAbsolute(lastGood, baseDir);

        // 4. exe 旁邊就有 Config（把 DeltaAxis 部署進主程式資料夾時）
        yield return Path.Combine(baseDir, "Config");

        // 5. exe 在主程式部署資料夾底下的子資料夾時，Config 在上一層 / 上兩層
        yield return ToAbsolute(@"..\Config", baseDir);
        yield return ToAbsolute(@"..\..\Config", baseDir);

        // 6. exe 旁邊有 Slot_Inspection\Config（兩支程式並排部署時）
        yield return Path.Combine(baseDir, "Slot_Inspection", "Config");
        yield return ToAbsolute(@"..\Slot_Inspection\Config", baseDir);

        // 7. 開發機：從 DeltaAxis 的 bin 往上四層到 repo 根，再進主程式建置輸出 / 原始碼
        yield return ToAbsolute(@"..\..\..\..\Slot_Inspection\bin\Debug\net8.0-windows\Config", baseDir);
        yield return ToAbsolute(@"..\..\..\..\Slot_Inspection\bin\Release\net8.0-windows\Config", baseDir);
        yield return ToAbsolute(@"..\..\..\..\Slot_Inspection\Config", baseDir);

        // 8. 機台固定的網路芳鄰共用路徑
        yield return MachineSharePath;
    }

    /// <summary>
    /// 找出第一個「真的存在且檔案齊全」的 Config 資料夾；全部找不到回傳 null。
    /// </summary>
    public static string? Resolve(string? uiHint, string? lastGood)
    {
        foreach (var folder in CandidateFolders(uiHint, lastGood))
        {
            if (IsValidConfigFolder(folder))
                return Path.GetFullPath(folder);
        }
        return null;
    }

    /// <summary>該資料夾存在，且同時有 InspectionConfig.json 與 SlotPositions.json。</summary>
    public static bool IsValidConfigFolder(string? folder)
        => !string.IsNullOrWhiteSpace(folder)
           && Directory.Exists(folder)
           && File.Exists(Path.Combine(folder, "InspectionConfig.json"))
           && File.Exists(Path.Combine(folder, "SlotPositions.json"));

    private static string ToAbsolute(string path, string baseDir)
        => Path.IsPathFullyQualified(path)
            ? path
            : Path.GetFullPath(Path.Combine(baseDir, path));
}
