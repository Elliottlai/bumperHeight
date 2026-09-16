using System.IO;
using System.Reflection;

namespace Slot_Inspection.Services;

/// <summary>
/// 每次 RUN 結束寫一行週期時間統計到 CSV（logs\Timing\RunTiming_yyyyMMdd.csv），
/// 方便用 Excel 直接比較優化前後每次 RUN 的總耗時與各階段耗時。
/// 只做記錄，寫檔失敗只回傳錯誤訊息給呼叫端記 log，不影響流程。
/// </summary>
public static class RunTimingCsvWriter
{
    private static readonly object _lock = new();

    private const string Header =
        "結束時間,版本號,條碼,完成Slot數,狀態,總耗時s,平均s每Slot,RUN前置s,含前置總計s," +
        "Y移動s,Z下降s,Z上升s,拍照+分析s,其中光源重設s,其中到位防振等待s,其中取像s,其中存原圖AI分析s,其中寫CSV畫線圖畫面更新s,其中補滿延遲s," +
        "側別判斷s,其他s,拍照下限設定ms,基準s每Slot,客戶目標s," +
        "Z安全確認次數,ZL最小餘裕mm,ZR最小餘裕mm,Y最大偏差mm,ZL最大偏差mm,ZR最大偏差mm,位置讀值自我檢查,本次優化設定";

    /// <summary>寫入一行；成功回傳 null，失敗回傳錯誤訊息。</summary>
    /// <param name="preRunMs">進入 DryRunAsync 之前的 RUN 前置時間（讀條碼、MES 等），0 表示沒有量測。</param>
    /// <param name="optimizationNote">本次 RUN 實際生效的優化設定說明（跟 [Timing] 摘要同一段文字），供事後對照。</param>
    public static string? Append(
        RunTimingRecorder timing, RunSafetyStats safety,
        string barcode, int slotCount, bool completed,
        int captureMinDurationMs, double baselineSecPerSlot, double targetSec,
        long preRunMs = 0, string optimizationNote = "")
    {
        try
        {
            var now = DateTime.Now;
            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", "Timing");
            string path = Path.Combine(dir, $"RunTiming_{now:yyyyMMdd}.csv");

            string version = Assembly.GetEntryAssembly()?
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
            double totalSec = timing.TotalMs / 1000.0;
            double preRunSec = preRunMs / 1000.0;

            string row = string.Join(",",
                Escape(now.ToString("yyyy-MM-dd HH:mm:ss")),
                Escape(version),
                Escape(barcode),
                slotCount,
                completed ? "完成" : "中斷",
                totalSec.ToString("F1"),
                slotCount > 0 ? (totalSec / slotCount).ToString("F2") : "",
                preRunMs > 0 ? preRunSec.ToString("F1") : "",
                preRunMs > 0 ? (totalSec + preRunSec).ToString("F1") : "",
                Sec(timing.GetStageMs("Y移動")),
                Sec(timing.GetStageMs("Z下降")),
                Sec(timing.GetStageMs("Z上升")),
                Sec(timing.GetStageMs("拍照+分析")),
                Sec(timing.GetDetailMs("光源重設")),
                Sec(timing.GetDetailMs("到位防振等待")),
                Sec(timing.GetDetailMs("取像")),
                Sec(timing.GetDetailMs("存原圖+AI分析")),
                Sec(timing.GetDetailMs("寫CSV/畫線圖/畫面更新")),
                Sec(timing.GetDetailMs("補滿延遲")),
                Sec(timing.GetStageMs("側別判斷")),
                Sec(timing.OtherMs),
                captureMinDurationMs,
                baselineSecPerSlot > 0 ? baselineSecPerSlot.ToString("F2") : "",
                targetSec > 0 ? targetSec.ToString("F1") : "",
                safety.ZSafeCheckCount,
                Mm(safety.MinMarginZL),
                Mm(safety.MinMarginZR),
                Mm(safety.GetMaxDeviation("AxisY")),
                Mm(safety.GetMaxDeviation("AxisZL")),
                Mm(safety.GetMaxDeviation("AxisZR")),
                safety.ReadbackStatus,
                Escape(optimizationNote));

            lock (_lock)
            {
                Directory.CreateDirectory(dir);
                bool needsHeader = !File.Exists(path) || new FileInfo(path).Length == 0;
                // UTF8（含 BOM）：Excel 直接開啟中文不會亂碼，跟 InspectionResultWriter 一致
                using var sw = new StreamWriter(path, append: true, System.Text.Encoding.UTF8);
                if (needsHeader)
                    sw.WriteLine(Header);
                sw.WriteLine(row);
            }
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    private static string Sec(long ms) => (ms / 1000.0).ToString("F2");

    private static string Mm(double v) =>
        double.IsNaN(v) || double.IsInfinity(v) ? "" : v.ToString("F2");

    private static string Escape(string s) =>
        s.Contains(',') || s.Contains('"') ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
}
