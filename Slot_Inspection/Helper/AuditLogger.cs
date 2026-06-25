using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Slot_Inspection.Models;

namespace Slot_Inspection.Helper;

/// <summary>
/// 將使用者操作記錄寫入執行檔旁的 Logs\AuditLog\ 資料夾，仿照 CancelEventLogger 的寫法。
/// 每天一個檔案：AuditLog_yyyyMMdd.log
/// </summary>
public static class AuditLogger
{
    private static readonly string LogDir = Path.Combine(
        AppContext.BaseDirectory, "Logs", "AuditLog");

    /// <summary>給日誌檢視視窗用的單筆紀錄。</summary>
    public sealed record Entry(DateTime Time, string Username, string Role, string Action);

    private static readonly Regex LinePattern = new(
        @"^\[(?<time>[\d\-: ]+)\]\s*\[(?<user>[^\]]*)\]\s*\[(?<role>[^\]]*)\]\s*(?<action>.*)$",
        RegexOptions.Compiled);

    /// <summary>記錄一筆操作。</summary>
    /// <param name="username">操作者帳號（登入失敗時為嘗試登入的帳號名）</param>
    /// <param name="role">操作者角色（登入失敗時可傳 null）</param>
    /// <param name="action">人類可讀的動作描述</param>
    public static void Write(string username, UserRole? role, string action)
    {
        try
        {
            Directory.CreateDirectory(LogDir);
            string filePath = Path.Combine(LogDir, $"AuditLog_{DateTime.Now:yyyyMMdd}.log");

            string roleText = role?.ToString() ?? "-";
            string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{username}] [{roleText}] {action}";
            File.AppendAllText(filePath, line + Environment.NewLine, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AuditLog] 寫入失敗：{ex.Message}");
        }
    }

    /// <summary>讀取指定日期的所有紀錄（檔案不存在則回傳空清單）。供日誌檢視視窗使用。</summary>
    public static List<Entry> ReadEntries(DateTime date)
    {
        var result = new List<Entry>();
        string filePath = Path.Combine(LogDir, $"AuditLog_{date:yyyyMMdd}.log");
        if (!File.Exists(filePath)) return result;

        foreach (var line in File.ReadAllLines(filePath, Encoding.UTF8))
        {
            var match = LinePattern.Match(line);
            if (!match.Success) continue;
            if (!DateTime.TryParse(match.Groups["time"].Value, out var time)) continue;

            result.Add(new Entry(time, match.Groups["user"].Value, match.Groups["role"].Value, match.Groups["action"].Value));
        }
        return result;
    }

    /// <summary>列出目前所有有紀錄的日期（依檔名解析，新到舊排序），供日誌檢視視窗下拉選單使用。</summary>
    public static List<DateTime> ListAvailableDates()
    {
        var result = new List<DateTime>();
        if (!Directory.Exists(LogDir)) return result;

        foreach (var file in Directory.GetFiles(LogDir, "AuditLog_*.log"))
        {
            string name = Path.GetFileNameWithoutExtension(file);
            string datePart = name.Replace("AuditLog_", "");
            if (DateTime.TryParseExact(datePart, "yyyyMMdd", null,
                    System.Globalization.DateTimeStyles.None, out var date))
                result.Add(date);
        }
        result.Sort((a, b) => b.CompareTo(a));
        return result;
    }
}
