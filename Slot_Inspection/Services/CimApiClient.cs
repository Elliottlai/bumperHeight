using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Slot_Inspection.Services;

/// <summary>
/// MES WebAPI 用戶端。
/// 失敗時只寫 Log，不拋出例外，不影響機器主流程。
/// </summary>
public class CimApiClient
{
    private static readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(5)
    };

    private const string BaseUrl = "http://localhost:5000/api/CIMapi";

    private static readonly string _logDir = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "WebApiLog");

    // ── 最後一次連線狀態（供 ViewModel 更新 UI）────────────────
    public MesConnectionStatus LastStatus { get; private set; } = MesConnectionStatus.Unknown;

    // ═══════════════════════════════════════════════════════════
    //  Log 寫入（每天一個檔案）
    // ═══════════════════════════════════════════════════════════
    private static void WriteLog(string level, string message)
    {
        try
        {
            Directory.CreateDirectory(_logDir);
            string path = Path.Combine(_logDir, $"webapi_{DateTime.Now:yyyy-MM-dd}.log");
            string line = $"[{DateTime.Now:HH:mm:ss.fff}] [{level,-5}] {message}";
            File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
            System.Diagnostics.Debug.WriteLine($"[CIM] {line}");
        }
        catch { /* Log 失敗不影響流程 */ }
    }

    // ═══════════════════════════════════════════════════════════
    //  API 1：QuerySerialNoData — 讀碼成功後查詢工單資訊
    // ═══════════════════════════════════════════════════════════
    public async Task<string?> QueryWorkOrderAsync(string equipmentId, string productId)
    {
        WriteLog("INFO", $"→ QuerySerialNoData | EquipmentId={equipmentId} | ProductId={productId}");
        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                EventType   = "QuerySerialNoData",
                RecordId    = Guid.NewGuid().ToString("N"),
                EquipmentId = equipmentId,
                ProductId   = productId
            });

            var resp = await _http.PostAsync(
                $"{BaseUrl}/QuerySerialNoData",
                new StringContent(payload, Encoding.UTF8, "application/json"));

            var body = await resp.Content.ReadAsStringAsync();
            WriteLog("INFO", $"← QuerySerialNoData | HTTP {(int)resp.StatusCode} | {body}");

            if (!resp.IsSuccessStatusCode)
            {
                LastStatus = MesConnectionStatus.Disconnected;
                return null;
            }

            using var doc = JsonDocument.Parse(body);
            var workOrder = doc.RootElement
                              .GetProperty("QueryData")
                              .GetProperty("WorkerOrderNo")
                              .GetString();

            LastStatus = MesConnectionStatus.Connected;
            WriteLog("INFO", $"QuerySerialNoData OK | WorkerOrderNo={workOrder}");
            return workOrder;
        }
        catch (Exception ex)
        {
            LastStatus = MesConnectionStatus.Disconnected;
            WriteLog("ERROR", $"QuerySerialNoData 失敗 | {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  API 2：MeasureResultUpload — 統計完成後上傳量測結果
    // ═══════════════════════════════════════════════════════════
    public async Task UploadResultAsync(string equipmentId, string productId, bool pass)
    {
        string judge = pass ? "OK" : "NG";
        WriteLog("INFO", $"→ MeasureResultUpload | EquipmentId={equipmentId} | ProductId={productId} | TotalJudge={judge}");
        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                EventType         = "MeasureResultUpload",
                RecordId          = Guid.NewGuid().ToString("N"),
                EquipmentId       = equipmentId,
                ProductId         = productId,
                TotalJudge        = judge,
                ProcessStartTime  = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                ProcessEndTime    = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                MeasureResultList = new[]
                {
                    new { ItemName = "OverallJudge", Judge = judge, Value = pass ? "1" : "0" }
                }
            });

            var resp = await _http.PostAsync(
                $"{BaseUrl}/MeasureResultUpload",
                new StringContent(payload, Encoding.UTF8, "application/json"));

            var body = await resp.Content.ReadAsStringAsync();
            WriteLog("INFO", $"← MeasureResultUpload | HTTP {(int)resp.StatusCode} | {body}");

            LastStatus = resp.IsSuccessStatusCode
                ? MesConnectionStatus.Connected
                : MesConnectionStatus.Disconnected;
        }
        catch (Exception ex)
        {
            LastStatus = MesConnectionStatus.Disconnected;
            WriteLog("ERROR", $"MeasureResultUpload 失敗 | {ex.GetType().Name}: {ex.Message}");
        }
    }
}

/// <summary>MES 連線狀態列舉。</summary>
public enum MesConnectionStatus
{
    Unknown,      // 尚未嘗試連線
    Connected,    // 上一次呼叫成功
    Disconnected  // 上一次呼叫失敗
}
