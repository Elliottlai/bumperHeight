using BumperFlat.ImageProcessing;
using BumperFlat.ImageProcessing.Models;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Slot_Inspection.Services;

/// <summary>
/// 檢測結果輸出服務 — 將每個 Slot 的影像演算法結果寫入 CSV 報表。
/// FoupID 欄位寫入條碼讀取器所讀到的 barcode 值。
/// CSV 欄位：Date, FoupID, Bumper Position, Layer, Item Name, Value,
///           LowerSpecLimit, UpperSpecLimit, Judgment, ProcessStartTime, ProcessEndTime, DuringTime
/// </summary>
public static class InspectionResultWriter
{
    private static readonly object _csvLock = new();
    private static readonly HashSet<string> _initializedCsvPaths = new();
    private static readonly Dictionary<string, string> _csvPathByFoupId = new();
    private static readonly Regex _timestampRegex = new(
        @"\d{4}-\d{2}-\d{2}T\d{2}-\d{2}-\d{2}",
        RegexOptions.Compiled);

    // CSV 標頭 — FoupID 欄位填入條碼讀取器讀到的值
    private const string CsvHeader =
        "Date,FoupID,Bumper Position,Layer,Item Name,Value," +
        "LowerSpecLimit,UpperSpecLimit,Judgment,ProcessStartTime,ProcessEndTime,DuringTime";

    // ---------------------------------------------------------------
    // 公開方法
    // ---------------------------------------------------------------

    /// <summary>
    /// 使用 BumperFlat.ImageProcessing 的 CSV 功能，
    /// 將 ProcessingResult（含 AdditionalData）寫入 CSV。
    /// </summary>
    /// <param name="foupId">條碼讀取器讀到的 barcode 值，對應 CSV 的 FoupID 欄位。</param>
    /// <param name="bumperPosition">Bumper 位置字串。</param>
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

        lock (_csvLock)
        {
            string csvPath = GetOrCreateCsvPath(foupId, imagePath, now);
            EnsureCsvInitialized(csvPath);

            var processor = new ImageProcessor();

            int flatWritten = 0;
            if (flatProcessingResult != null)
            {
                flatWritten = processor.WriteResultToCsv(
                    imagePath, csvPath, flatProcessingResult,
                    bumperPosition, null, null, null);
            }

            int defectWritten = 0;
            if (defectProcessingResult != null)
            {
                defectWritten = processor.WriteResultToCsv(
                    imagePath, csvPath, defectProcessingResult,
                    bumperPosition, null, null, null);
            }

            System.Diagnostics.Debug.WriteLine(
                $"[ResultWriter] AnalyzeCSV FoupID={foupId} | {bumperPosition} | " +
                $"Flat={flatWritten}, Defect={defectWritten} | CSV: {csvPath}");
        }
    }

    /// <summary>
    /// Fallback 手動寫入：不依賴 DLL，直接以標準欄位格式寫入一行資料。
    /// </summary>
    /// <param name="barcode">條碼讀取器讀到的值，寫入 CSV 的 FoupID 欄位。</param>
    /// <param name="slotName">Slot 名稱，寫入 Item Name 欄位。</param>
    /// <param name="value">量測值。</param>
    /// <param name="isNg">是否 NG，寫入 Judgment 欄位。</param>
    /// <param name="imagePath">來源影像完整路徑（用於抽取時間戳記）。</param>
    /// <param name="bumperPosition">Bumper 位置（選填）。</param>
    /// <param name="layer">層別（選填）。</param>
    /// <param name="lowerSpec">規格下限（選填）。</param>
    /// <param name="upperSpec">規格上限（選填）。</param>
    /// <param name="processStartTime">處理開始時間（選填，用於計算 DuringTime）。</param>
    public static void WriteSlotResult(
        string barcode,
        string slotName,
        double value,
        bool isNg,
        string imagePath,
        string bumperPosition = "",
        string layer = "",
        double? lowerSpec = null,
        double? upperSpec = null,
        DateTime? processStartTime = null)
    {
        var now = DateTime.Now;

        lock (_csvLock)
        {
            string csvPath = GetOrCreateCsvPath(barcode, imagePath, now);
            bool needsHeader = !File.Exists(csvPath) || new FileInfo(csvPath).Length == 0;

            using var sw = new StreamWriter(csvPath, append: true, System.Text.Encoding.UTF8);

            if (needsHeader)
                sw.WriteLine(CsvHeader);

            var start = processStartTime ?? now;
            var duration = (now - start).TotalSeconds;

            // barcode（讀碼器讀值）直接寫入 FoupID 欄位
            sw.WriteLine(string.Join(",",
                CsvEscape(now.ToString("yyyy-MM-dd")),
                CsvEscape(barcode),           // FoupID = barcode 讀取值
                CsvEscape(bumperPosition),
                CsvEscape(layer),
                CsvEscape(slotName),
                value.ToString("F4"),
                lowerSpec.HasValue ? lowerSpec.Value.ToString("F4") : "",
                upperSpec.HasValue ? upperSpec.Value.ToString("F4") : "",
                isNg ? "NG" : "OK",
                CsvEscape(start.ToString("HH:mm:ss.fff")),
                CsvEscape(now.ToString("HH:mm:ss.fff")),
                processStartTime.HasValue ? duration.ToString("F3") : ""
            ));

            System.Diagnostics.Debug.WriteLine(
                $"[ResultWriter] FoupID={barcode} | {slotName} | {value:F4} | " +
                $"{(isNg ? "NG" : "OK")} | CSV: {csvPath}");
        }
    }

    /// <summary>
    /// 批次結束後寫入彙總結果（SUMMARY 行），並清除該 FoupId 的快取。
    /// </summary>
    /// <param name="barcode">條碼讀取器讀到的值（對應 FoupID）。</param>
    /// <param name="overallPass">整批是否 PASS。</param>
    public static void WriteSummary(string barcode, bool overallPass)
    {
        var now = DateTime.Now;

        lock (_csvLock)
        {
            string csvPath = GetOrCreateCsvPath(barcode, null, now);
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
                $"[ResultWriter] SUMMARY FoupID={barcode} = {(overallPass ? "PASS" : "FAIL")} " +
                $"| written={written} | CSV: {csvPath}");

            _csvPathByFoupId.Remove(SanitizeFileName(barcode));
        }
    }

    // ---------------------------------------------------------------
    // 私有輔助方法
    // ---------------------------------------------------------------

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
    /// RFC 4180 CSV 逸出：含逗號、雙引號或換行時以雙引號包裹。
    /// </summary>
    private static string CsvEscape(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        if (value.Contains(',') || value.Contains('"')
            || value.Contains('\n') || value.Contains('\r'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }
}
