using BumperFlat.ImageProcessing;
using BumperFlat.ImageProcessing.Models;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Slot_Inspection.Services;

/// <summary>
/// 檢測結果輸出服務 — 將每個 Slot 的影像演算法結果寫入 CSV 報表。
/// 
/// CSV 欄位：Date, FoupID, Bumper Position, Layer, Item Name, Value,
///           LowerSpecLimit, UpperSpecLimit, Judgment, ProcessStartTime, ProcessEndTime, DuringTime
/// 
/// 資料來源：
///   - Date / ProcessStartTime / ProcessEndTime / DuringTime → DLL 執行演算法時自動填入 ProcessingResult
///   - FoupID → 由呼叫端傳入的 foupId（條碼讀取器讀值）
///   - Bumper Position → 由呼叫端傳入的 bumperPosition 字串
///   - Layer / Item Name / Value / LowerSpecLimit / UpperSpecLimit / Judgment
///       → DLL 執行 BumpFlatDetection / DefectDetection 後自動寫入 ProcessingResult.AdditionalData，
///         再由 WriteResultToCsv 負責寫出到 CSV
/// </summary>
public static class InspectionResultWriter
{
    private static readonly object _csvLock = new();
    private static readonly HashSet<string> _initializedCsvPaths = new();
    private static readonly Dictionary<string, string> _csvPathByFoupId = new();
    private static readonly Regex _timestampRegex = new(
        @"\d{4}-\d{2}-\d{2}T\d{2}-\d{2}-\d{2}",
        RegexOptions.Compiled);

    // ---------------------------------------------------------------
    // 公開方法
    // ---------------------------------------------------------------

    /// <summary>
    /// 主要寫入方法：使用 BumperFlat.ImageProcessing 的 CSV 功能，
    /// 將 ProcessingResult（含 AdditionalData）寫入 CSV。
    /// 
    /// DLL 會自動從 ProcessingResult 中取出：
    /// Layer, Item Name, Value, LowerSpecLimit, UpperSpecLimit, Judgment,
    /// Date, ProcessStartTime, ProcessEndTime, DuringTime
    /// 並寫入對應的 CSV 欄位。
    /// </summary>
    /// <param name="foupId">條碼讀取器讀到的 barcode 值，對應 CSV 的 FoupID 欄位。</param>
    /// <param name="bumperPosition">Bumper 位置字串，對應 CSV 的 Bumper Position 欄位。</param>
    /// <param name="imagePath">來源影像完整路徑。</param>
    /// <param name="flatProcessingResult">Flat 演算法結果；null 時略過。</param>
    /// <param name="defectProcessingResult">Defect 演算法結果；null 時略過。</param>
    public static void WriteAnalyzeResult(
        string foupId,
        string bumperPosition,
        string imagePath,
        ProcessingResult? flatProcessingResult,
        ProcessingResult? defectProcessingResult = null)
    {
        if ((flatProcessingResult == null && defectProcessingResult == null)
            || string.IsNullOrWhiteSpace(imagePath))
            return;

        var now = DateTime.Now;
        string sanitizedImagePath = SanitizeImagePath(imagePath);

        lock (_csvLock)
        {
            string csvPath = GetOrCreateCsvPath(foupId, sanitizedImagePath, now);
            EnsureCsvInitialized(csvPath);

            var processor = new ImageProcessor();

            int flatWritten = 0;
            if (flatProcessingResult != null)
            {
                flatWritten = processor.WriteResultToCsv(
                    sanitizedImagePath, csvPath, flatProcessingResult,
                    bumperPosition, null, null, null);
            }

            int defectWritten = 0;
            if (defectProcessingResult != null)
            {
                defectWritten = processor.WriteResultToCsv(
                    sanitizedImagePath, csvPath, defectProcessingResult,
                    bumperPosition, null, null, null);
            }

            PatchFoupIdInCsv(csvPath, foupId);

            System.Diagnostics.Debug.WriteLine(
                $"[ResultWriter] AnalyzeCSV FoupID={foupId} | {bumperPosition} | " +
                $"Flat={flatWritten}, Defect={defectWritten} | CSV: {csvPath}");
        }
    }

    /// <summary>
    /// Fallback 手動寫入：當 DLL 無法正常執行時，以最少資訊寫入一行資料。
    /// 僅填入 Date, FoupID, Item Name, Value, Judgment 等基本欄位。
    /// </summary>
    /// <param name="foupId">條碼讀取器讀到的值，寫入 CSV 的 FoupID 欄位。</param>
    /// <param name="slotName">Slot 名稱，寫入 Item Name 欄位。</param>
    /// <param name="value">量測值。</param>
    /// <param name="isNg">是否 NG，寫入 Judgment 欄位。</param>
    /// <param name="imagePath">來源影像完整路徑（用於決定 CSV 路徑）。</param>
    public static void WriteSlotResult(
        string foupId,
        string slotName,
        double value,
        bool isNg,
        string imagePath)
    {
        var now = DateTime.Now;

        lock (_csvLock)
        {
            string csvPath = GetOrCreateCsvPath(foupId, imagePath, now);
            EnsureCsvInitialized(csvPath);

            var processor = new ImageProcessor();
            var items = new List<CsvResultWriter.MeasurementItem>
            {
                new() { SequenceNo = 1, ItemName = slotName, Value = value }
            };

            string label = isNg ? "NG" : "OK";

            int written = processor.WriteCustomResultToCsv(
                imagePath, csvPath, items, label,
                null, null, null);

            PatchFoupIdInCsv(csvPath, foupId);

            System.Diagnostics.Debug.WriteLine(
                $"[ResultWriter] Fallback FoupID={foupId} | {label} | written={written} | CSV: {csvPath}");
        }
    }

    /// <summary>
    /// 批次結束後寫入彙總結果（SUMMARY 行），並清除該 FoupId 的快取。
    /// </summary>
    /// <param name="foupId">條碼讀取器讀到的值（對應 FoupID）。</param>
    /// <param name="overallPass">整批是否 PASS。</param>
    public static void WriteSummary(string foupId, bool overallPass)
    {
        var now = DateTime.Now;

        lock (_csvLock)
        {
            string csvPath = GetOrCreateCsvPath(foupId, null, now);
            EnsureCsvInitialized(csvPath);

            var processor = new ImageProcessor();
            var items = new List<CsvResultWriter.MeasurementItem>
            {
                new() { SequenceNo = 1, ItemName = "OverallPass", Value = overallPass ? 1 : 0 }
            };

            int written = processor.WriteCustomResultToCsv(
                string.Empty, csvPath, items, "SUMMARY",
                null, null, null);

            System.Diagnostics.Debug.WriteLine(
                $"[ResultWriter] SUMMARY FoupID={foupId} = {(overallPass ? "PASS" : "FAIL")} " +
                $"| written={written} | CSV: {csvPath}");

            _csvPathByFoupId.Remove(SanitizeFileName(foupId));
        }
    }

    // ---------------------------------------------------------------
    // 私有輔助方法
    // ---------------------------------------------------------------

    /// <summary>
    /// 清理 imagePath 檔名中 FoupID 區段多餘的底線，
    /// 確保檔名符合 4 段格式：{日期}_{FoupID}_{BumperPosition}_{LayerToken}。
    /// </summary>
    private static string SanitizeImagePath(string imagePath)
    {
        string dir = Path.GetDirectoryName(imagePath) ?? string.Empty;
        string ext = Path.GetExtension(imagePath);
        string nameWithoutExt = Path.GetFileNameWithoutExtension(imagePath);

        string[] parts = nameWithoutExt.Split('_');

        // 需要至少 4 段才有清理的必要
        if (parts.Length <= 4)
            return imagePath;

        // 第 1 段 = 日期（固定格式），最後 2 段 = BumperPosition + LayerToken
        // 中間剩餘所有段合併（不加底線）= FoupID
        string datePart      = parts[0];
        string bumperPart    = parts[parts.Length - 2];
        string layerPart     = parts[parts.Length - 1];
        string foupIdPart    = string.Concat(parts[1..(parts.Length - 2)]);

        string sanitizedName = $"{datePart}_{foupIdPart}_{bumperPart}_{layerPart}{ext}";
        return string.IsNullOrEmpty(dir)
            ? sanitizedName
            : Path.Combine(dir, sanitizedName);
    }

    /// <summary>
    /// 確保 CSV 已初始化（只呼叫一次 DLL InitializeCsvFile）。
    /// </summary>
    private static void EnsureCsvInitialized(string csvPath)
    {
        if (_initializedCsvPaths.Contains(csvPath)) return;
        new ImageProcessor().InitializeCsvFile(csvPath);
        _initializedCsvPaths.Add(csvPath);
    }

    /// <summary>
    /// 取得（或建立）指定 FoupId 對應的 CSV 路徑。
    /// 同一 FoupId 整批只建立一次，後續均從快取取得。
    /// </summary>
    private static string GetOrCreateCsvPath(string foupId, string? imagePath, DateTime fallbackNow)
    {
        string safeFoupId = SanitizeFileName(foupId);

        if (_csvPathByFoupId.TryGetValue(safeFoupId, out var existingPath))
            return existingPath;

        string timestampText = TryExtractTimestampText(imagePath)
            ?? fallbackNow.ToString("yyyy-MM-ddTHH-mm-ss");

        string datePart = timestampText.Length >= 10
            ? timestampText[..10].Replace("-", "")
            : fallbackNow.ToString("yyyyMMdd");

        string baseDir = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "Results", datePart);
        Directory.CreateDirectory(baseDir);

        string csvPath = Path.Combine(baseDir, $"{safeFoupId}_{timestampText}.csv");
        _csvPathByFoupId[safeFoupId] = csvPath;
        return csvPath;
    }

    /// <summary>
    /// 從影像檔名（不含副檔名）抽取 yyyy-MM-ddTHH-mm-ss 時間戳記。
    /// </summary>
    private static string? TryExtractTimestampText(string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath)) return null;
        string fileName = Path.GetFileNameWithoutExtension(imagePath);
        if (string.IsNullOrWhiteSpace(fileName)) return null;
        var match = _timestampRegex.Match(fileName);
        return match.Success ? match.Value : null;
    }

    /// <summary>
    /// 將字串中不合法的檔名字元替換為底線，避免 CSV 檔名錯誤。
    /// </summary>
    private static string SanitizeFileName(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "UNKNOWN";
        foreach (char c in Path.GetInvalidFileNameChars())
            input = input.Replace(c, '_');
        return input;
    }

    /// <summary>
    /// 讀取 CSV 並將 FoupID 欄位空白的資料列填入指定的 barcode 值。
    /// 不依賴 DLL，直接對 CSV 檔案操作。
    /// </summary>
    private static void PatchFoupIdInCsv(string csvPath, string foupId)
    {
        if (!File.Exists(csvPath) || string.IsNullOrWhiteSpace(foupId)) return;

        var lines = File.ReadAllLines(csvPath);
        if (lines.Length == 0) return;

        // 找出 FoupID 欄位索引
        var headers = lines[0].Split(',');
        int foupIdIndex = Array.FindIndex(headers, h =>
            h.Trim().Equals("FoupID", StringComparison.OrdinalIgnoreCase));

        if (foupIdIndex < 0) return;

        bool changed = false;
        for (int i = 1; i < lines.Length; i++)
        {
            var cols = lines[i].Split(',');
            if (cols.Length <= foupIdIndex) continue;

            if (string.IsNullOrWhiteSpace(cols[foupIdIndex]))
            {
                cols[foupIdIndex] = foupId;
                lines[i] = string.Join(",", cols);
                changed = true;
            }
        }

        if (changed)
            File.WriteAllLines(csvPath, lines);
    }
}
