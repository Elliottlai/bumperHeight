using System.IO;
using System.Text.Json;

namespace DeltaAxis_RS485.Services;

/// <summary>
/// DeltaAxis_RS485 自己的小設定檔（存在 exe 旁的 DeltaAxisSettings.json）。
/// 目前只記兩件事：上次成功讀到的 Slot_Inspection Config 資料夾、以及是否自動跟隨主程式變更。
/// 讓下次開機不用重找路徑、也記住使用者的偏好。
/// </summary>
public sealed class DeltaAxisSettings
{
    /// <summary>上次成功讀取安全設定的 Slot_Inspection Config 資料夾絕對路徑。</summary>
    public string? SlotInspectionConfigFolder { get; set; }

    /// <summary>主程式存檔修改設定檔時，是否自動重新載入（預設開）。</summary>
    public bool AutoFollowSlotInspectionConfig { get; set; } = true;

    private static readonly string FilePath =
        Path.Combine(AppContext.BaseDirectory, "DeltaAxisSettings.json");

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static DeltaAxisSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<DeltaAxisSettings>(File.ReadAllText(FilePath), JsonOpts)
                       ?? new DeltaAxisSettings();
        }
        catch
        {
            // 檔案壞掉就當作沒有，用預設值
        }
        return new DeltaAxisSettings();
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOpts));
        }
        catch
        {
            // 存不了（權限、磁碟）不影響主功能，靜默略過
        }
    }
}
