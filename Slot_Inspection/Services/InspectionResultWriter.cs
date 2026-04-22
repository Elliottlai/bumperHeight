namespace Slot_Inspection.Services;

/// <summary>
/// 檢測結果輸出服務 — 存 CSV、存圖、上傳 MES...
/// ?? TODO：實作真實的輸出邏輯。
/// 目前為 Stub，只輸出到 Debug。
/// </summary>
public static class InspectionResultWriter
{
    /// <summary>
    /// 將單一 Slot 的結果寫入 CSV。
    /// ?? TODO：實作 CSV 寫入邏輯。
    /// </summary>
    public static void WriteSlotResult(
        string barcode,
        string slotName,
        double value,
        bool isNg,
        string imagePath)
    {
        // ?? TODO：實作 CSV 寫入，例如：
        //
        // string csvPath = Path.Combine(@"D:\Results", $"{DateTime.Now:yyyyMMdd}", $"{barcode}.csv");
        // Directory.CreateDirectory(Path.GetDirectoryName(csvPath)!);
        // bool needsHeader = !File.Exists(csvPath);
        // using var sw = new StreamWriter(csvPath, append: true);
        // if (needsHeader) sw.WriteLine("Time,Barcode,Slot,Value,Result,ImagePath");
        // sw.WriteLine($"{DateTime.Now:HH:mm:ss},{barcode},{slotName},{value:F4},{(isNg?"NG":"OK")},{imagePath}");

        // Stub：只輸出到 Debug
        System.Diagnostics.Debug.WriteLine(
            $"[ResultWriter] {barcode} | {slotName} | {value:F4} | {(isNg ? "NG" : "OK")} | {imagePath}");
    }

    /// <summary>
    /// 批次寫入整批檢測完成的彙總結果。
    /// ?? TODO：實作彙總報告邏輯。
    /// </summary>
    public static void WriteSummary(string barcode, bool overallPass)
    {
        // ?? TODO：寫彙總 CSV / 上傳 MES

        // Stub：只輸出到 Debug
        System.Diagnostics.Debug.WriteLine(
            $"[ResultWriter] SUMMARY {barcode} = {(overallPass ? "PASS" : "FAIL")}");
    }
}
