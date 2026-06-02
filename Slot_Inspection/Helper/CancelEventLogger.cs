using System;
using System.IO;
using System.Text;

namespace Slot_Inspection.Helper;

/// <summary>
/// 將取消 / 例外事件寫入執行檔旁的 Logs\CancelEvents\ 資料夾。
/// 每天一個檔案：CancelEvents_yyyyMMdd.log
/// </summary>
public static class CancelEventLogger
{
    private static readonly string LogDir = Path.Combine(
        AppContext.BaseDirectory, "Logs", "CancelEvents");

    /// <summary>
    /// 記錄一筆取消或例外事件，格式以中文為主。
    /// </summary>
    /// <param name="source">觸發來源（例如 OnStart、OnRunInspection、光閘X7）</param>
    /// <param name="reason">人類可讀的原因描述</param>
    /// <param name="ex">原始例外（可為 null）</param>
    public static void Write(string source, string reason, Exception? ex = null)
    {
        try
        {
            Directory.CreateDirectory(LogDir);
            string filePath = Path.Combine(LogDir, $"CancelEvents_{DateTime.Now:yyyyMMdd}.log");

            var sb = new StringBuilder();
            sb.AppendLine("============================================================");
            sb.AppendLine($"發生時間：{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
            sb.AppendLine($"觸發來源：{source}");
            sb.AppendLine($"原因說明：{reason}");

            if (ex != null)
            {
                sb.AppendLine($"例外類型：{ex.GetType().FullName}");
                sb.AppendLine($"例外訊息：{ex.Message}");
                if (ex.InnerException != null)
                    sb.AppendLine($"內部例外：{ex.InnerException.Message}");
                sb.AppendLine("呼叫堆疊：");
                sb.AppendLine(ex.StackTrace);
            }

            sb.AppendLine();
            File.AppendAllText(filePath, sb.ToString(), Encoding.UTF8);

            System.Diagnostics.Debug.WriteLine(
                $"[取消日誌] {source} | {reason} -> {filePath}");
        }
        catch (Exception writeEx)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[取消日誌] 寫入失敗：{writeEx.Message}");
        }
    }
}