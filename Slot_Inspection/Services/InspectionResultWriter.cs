using BumperFlat.ImageProcessing;
using BumperFlat.ImageProcessing.Models;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Slot_Inspection.Services;

/// <summary>
/// 檢測結果輸出服務 — 將每個 Slot 的影像演算法結果寫入 CSV 報表。
/// 
/// CSV 欄位：Date, FoupID, Bumper Position, Layer, Value,
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

    /// <summary>CSV 報表根目錄（預設為 exe 同層的 Results 資料夾，可由設定視窗覆寫）。</summary>
    public static string ResultsRootDirectory
    {
        get => _resultsRootDirectory;
        set => _resultsRootDirectory = string.IsNullOrWhiteSpace(value)
            ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Results")
            : value;
    }
    private static string _resultsRootDirectory =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Results");
    private static readonly Regex _timestampRegex = new(
        @"\d{4}-\d{2}-\d{2}T\d{2}-\d{2}-\d{2}",
        RegexOptions.Compiled);

    // CSV 欄位順序（不透過 DLL 直接以 StreamWriter 寫入時使用）
    private const string CsvHeader =
        "Date,FoupID,Bumper Position,Layer,Value," +
        "LowerSpecLimit,UpperSpecLimit,Judgment,ProcessStartTime,ProcessEndTime,DuringTime";

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

            PatchFoupIdInCsv(csvPath, foupId);

            System.Diagnostics.Debug.WriteLine(
                $"[ResultWriter] AnalyzeCSV FoupID={foupId} | {bumperPosition} | " +
                $"Flat={flatWritten}, Defect={defectWritten} | CSV: {csvPath}");
        }
    }

    /// <summary>
    /// 直接以 StreamWriter 寫入一行 Slot 結果（不依賴 DLL）。
    /// FoupID = barcode（條碼讀取器值），BumperPosition 由呼叫端傳入，
    /// Value 應由呼叫端先乘以換算係數後再傳入。
    /// </summary>
    public static void WriteSlotResult(
        string foupId,
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
            string csvPath = GetOrCreateCsvPath(foupId, imagePath, now);
            bool needsHeader = !File.Exists(csvPath) || new FileInfo(csvPath).Length == 0;

            using var sw = new StreamWriter(csvPath, append: true, System.Text.Encoding.UTF8);

            if (needsHeader)
                sw.WriteLine(CsvHeader);

            var start = processStartTime ?? now;
            double duration = (now - start).TotalSeconds;

            sw.WriteLine(string.Join(",",
                CsvEscape(now.ToString("yyyy-MM-dd")),
                CsvEscape(foupId),
                CsvEscape(bumperPosition),
                CsvEscape(layer),
                value.ToString("F6"),
                lowerSpec.HasValue ? lowerSpec.Value.ToString("F6") : "",
                upperSpec.HasValue ? upperSpec.Value.ToString("F6") : "",
                isNg ? "NG" : "OK",
                CsvEscape(start.ToString("HH:mm:ss.fff")),
                CsvEscape(now.ToString("HH:mm:ss.fff")),
                processStartTime.HasValue ? duration.ToString("F3") : ""
            ));

            System.Diagnostics.Debug.WriteLine(
                $"[ResultWriter] FoupID={foupId} | {bumperPosition} | Layer={layer} | " +
                $"{value:F6} | {(isNg ? "NG" : "OK")} | CSV: {csvPath}");
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
