using System.IO;
using System.Linq;
using Slot_Inspection.Models;

namespace Slot_Inspection.Services;

/// <summary>
/// QC 基準比對結果輸出服務。刻意跟一般量測報表（InspectionResultWriter）完全分開存放與分開格式，
/// 寫在 Results/QcCompare/{yyyyMMdd}/ 底下，避免兩種報表混在一起不好查看。
/// </summary>
public static class QcComparisonResultWriter
{
    private static readonly object _csvLock = new();

    private const string CsvHeader =
        "Timestamp,Barcode,RecipeName,Area,SlotName,BaselineValue,CurrentValue,DeltaValue," +
        "BaselineMeanIntensity,CurrentMeanIntensity,DeltaMeanIntensity," +
        "BaselineDistanceToRoiCenter,CurrentDistanceToRoiCenter,DeltaDistanceToRoiCenter,Result";

    /// <summary>把一次 QC 比對結果寫成一份獨立 CSV：每個 Slot 一行，最後補一行整體 SUMMARY。</summary>
    public static void Write(string barcode, string recipeName, IReadOnlyList<QcSlotComparison> rows)
    {
        if (rows.Count == 0) return;

        var now = DateTime.Now;
        string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Results", "QcCompare", now.ToString("yyyyMMdd"));
        Directory.CreateDirectory(dir);
        string csvPath = Path.Combine(dir, $"{SanitizeFileName(barcode)}_{now:yyyy-MM-ddTHH-mm-ss}.csv");

        lock (_csvLock)
        {
            using var sw = new StreamWriter(csvPath, append: false, System.Text.Encoding.UTF8);
            sw.WriteLine(CsvHeader);

            foreach (var r in rows)
                sw.WriteLine(string.Join(",",
                    CsvEscape(now.ToString("yyyy-MM-dd HH:mm:ss")), CsvEscape(barcode), CsvEscape(recipeName),
                    CsvEscape(r.Area), CsvEscape(r.Name),
                    r.BaselineValue.ToString("F3"), r.CurrentValue.ToString("F3"), r.DeltaValue.ToString("F3"),
                    r.BaselineMeanIntensity.ToString("F1"), r.CurrentMeanIntensity.ToString("F1"), r.DeltaMeanIntensity.ToString("F1"),
                    r.BaselineDistanceToRoiCenter.ToString("F3"), r.CurrentDistanceToRoiCenter.ToString("F3"), r.DeltaDistanceToRoiCenter.ToString("F3"),
                    r.ExceedsTolerance ? "NG" : "OK"));

            int exceedCount = rows.Count(r => r.ExceedsTolerance);
            sw.WriteLine(string.Join(",",
                CsvEscape(now.ToString("yyyy-MM-dd HH:mm:ss")), CsvEscape(barcode), CsvEscape(recipeName),
                "SUMMARY", "", "", "", "", "", "", "", "", "", "",
                CsvEscape($"共{rows.Count}個Slot，{exceedCount}個超差 → {(exceedCount > 0 ? "NG" : "OK")}")));
        }
    }

    private static string SanitizeFileName(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "UNKNOWN";
        foreach (char c in Path.GetInvalidFileNameChars())
            input = input.Replace(c, '_');
        return input;
    }

    /// <summary>RFC 4180 CSV 逸出：含逗號、雙引號或換行時以雙引號包裹。</summary>
    private static string CsvEscape(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }
}
