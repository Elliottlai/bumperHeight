using System.IO;
using System.Text.Json;
using DeltaAxis_RS485.Models;

namespace DeltaAxis_RS485.Services;

/// <summary>
/// 直接讀取 Slot_Inspection 主程式的 Config\SlotPositions.json / Config\InspectionConfig.json，
/// 彙整出 DeltaAxis_RS485 做安全警告要用的資料快照。
/// 採用「方案A」：DeltaAxis_RS485 自己定義輕量資料類別直接讀JSON檔案，不跟 Slot_Inspection 互相參考專案。
/// </summary>
public static class SlotInspectionConfigReader
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// 讀取並彙整安全參考快照。configFolderPath 應指向 Slot_Inspection 執行檔旁的 Config 資料夾。
    /// 任一檔案不存在或解析失敗都會丟例外，由呼叫端顯示錯誤訊息。
    /// </summary>
    public static SlotInspectionSafetySnapshot Load(string configFolderPath)
    {
        string slotPositionsPath = Path.Combine(configFolderPath, "SlotPositions.json");
        string inspectionConfigPath = Path.Combine(configFolderPath, "InspectionConfig.json");

        if (!File.Exists(slotPositionsPath))
            throw new FileNotFoundException($"找不到檔案：{slotPositionsPath}");
        if (!File.Exists(inspectionConfigPath))
            throw new FileNotFoundException($"找不到檔案：{inspectionConfigPath}");

        var slotPositions = JsonSerializer.Deserialize<SlotPositionsFile>(
            File.ReadAllText(slotPositionsPath), JsonOpts)
            ?? throw new InvalidDataException($"無法解析：{slotPositionsPath}");

        var inspectionConfig = JsonSerializer.Deserialize<InspectionConfigFile>(
            File.ReadAllText(inspectionConfigPath), JsonOpts)
            ?? throw new InvalidDataException($"無法解析：{inspectionConfigPath}");

        var knownYPositions = new List<double>();
        if (slotPositions.AreaA_Row1_R != null) knownYPositions.AddRange(slotPositions.AreaA_Row1_R);
        if (slotPositions.AreaA_Row1_L != null) knownYPositions.AddRange(slotPositions.AreaA_Row1_L);
        knownYPositions.Add(inspectionConfig.BarcodePositionY);

        return new SlotInspectionSafetySnapshot
        {
            KnownYPositions = knownYPositions,
            // R組、L組取較小值：DeltaAxis_RS485沒有PLC無法判斷目前是哪一板，取較保守（較容易先觸發警告）的門檻
            ZLCameraHeightLimit = Math.Min(inspectionConfig.CameraHeightZL_R, inspectionConfig.CameraHeightZL_L),
            ZRCameraHeightLimit = Math.Min(inspectionConfig.CameraHeightZR_R, inspectionConfig.CameraHeightZR_L),
        };
    }
}
