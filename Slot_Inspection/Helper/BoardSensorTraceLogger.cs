using System;
using System.IO;
using System.Text;

namespace Slot_Inspection.Helper;

/// <summary>
/// 持續追蹤 X3/X11 板子放置雷射感測「原始訊號」的變化，不受防抖/確認狀態影響，
/// 也不受 Run 狀態影響——只要訊號真的改變就記錄，因此可還原完整的 true/false 時間軸。
/// 每天一個檔案：Logs\BoardSensorTrace\BoardSensorTrace_yyyyMMdd.log
/// </summary>
public static class BoardSensorTraceLogger
{
    private static readonly string LogDir = Path.Combine(
        AppContext.BaseDirectory, "Logs", "BoardSensorTrace");

    /// <summary>
    /// 記錄一次 X3/X11 原始訊號變化。呼叫端負責判斷「有沒有變化才呼叫」。
    /// </summary>
    public static void Write(bool x3, bool x11)
    {
        try
        {
            Directory.CreateDirectory(LogDir);
            string filePath = Path.Combine(LogDir, $"BoardSensorTrace_{DateTime.Now:yyyyMMdd}.log");

            string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} | X3={x3,-5} | X11={x11,-5}";
            File.AppendAllText(filePath, line + Environment.NewLine, Encoding.UTF8);
        }
        catch (Exception writeEx)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[板子感測追蹤] 寫入失敗：{writeEx.Message}");
        }
    }
}
