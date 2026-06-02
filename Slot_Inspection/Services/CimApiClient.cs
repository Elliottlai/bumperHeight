using System.IO;
using System.Text;
using System.Text.Json;
using JsonType;
using WebApidll;

namespace Slot_Inspection.Services;

/// <summary>
/// MES WebAPI 用戶端，底層直接呼叫 WebApidll 函式。
/// 失敗時只寫 Log，不拋出例外，不影響機器主流程。
/// </summary>
public class CimApiClient
{
    // ★ WebApidll 實體，ConnectAsync 後才建立
    private WebApidll.WebApidll _api = null!;

    private string _baseUrl = "http://192.168.61.28:8069";

    /// <summary>設定 MES WebAPI 基底位址（例如 http://192.168.1.1:5000/api/CIMapi）。</summary>
    public string BaseUrl
    {
        get => _baseUrl;
        set => _baseUrl = value?.TrimEnd('/') ?? _baseUrl;
    }

    private string _equipmentId = "SlotInspection";

    /// <summary>設備識別碼，上拋時帶入 EquipmentId 欄位。</summary>
    public string EquipmentId
    {
        get => _equipmentId;
        set => _equipmentId = value ?? _equipmentId;
    }

    private string _productType = "SlotInspection";

    /// <summary>產品類型，對應 MES MeasureResultUpload 的 ProductType 欄位。</summary>
    public string ProductType
    {
        get => _productType;
        set => _productType = value ?? _productType;
    }

    private string _itemName = "Height";

    /// <summary>量測項目名稱，對應 MES MeasureResultUpload 的 ItemName 欄位。</summary>
    public string ItemName
    {
        get => _itemName;
        set => _itemName = value ?? _itemName;
    }

    private static readonly string _logDir = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "WebApiLog");

    // ── 設定檔路徑 ────────────────────────────────────────────
    private static readonly string _settingsPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "Config", "CimApiSettings.json");

    // ── 最後一次連線狀態（供 ViewModel 更新 UI）────────────────
    public MesConnectionStatus LastStatus { get; private set; } = MesConnectionStatus.Unknown;

    // ═══════════════════════════════════════════════════════════
    //  設定檔 Save / Load
    // ═══════════════════════════════════════════════════════════
    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
            var data = new CimApiSettings { BaseUrl = _baseUrl, EquipmentId = _equipmentId, ProductType = _productType, ItemName = _itemName };
            File.WriteAllText(_settingsPath,
                JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }),
                Encoding.UTF8);
        }
        catch (Exception ex)
        {
            WriteLog("ERROR", $"Save 設定失敗：{ex.Message}");
        }
    }

    public void Load()
    {
        try
        {
            if (!File.Exists(_settingsPath)) return;
            var data = JsonSerializer.Deserialize<CimApiSettings>(
                File.ReadAllText(_settingsPath, System.Text.Encoding.UTF8));
            if (data is null) return;
            if (!string.IsNullOrWhiteSpace(data.BaseUrl))
                _baseUrl = data.BaseUrl.TrimEnd('/');
            if (!string.IsNullOrWhiteSpace(data.EquipmentId))
                _equipmentId = data.EquipmentId;
            if (!string.IsNullOrWhiteSpace(data.ProductType))
                _productType = data.ProductType;
            if (!string.IsNullOrWhiteSpace(data.ItemName))
                _itemName = data.ItemName;
        }
        catch (Exception ex)
        {
            WriteLog("ERROR", $"Load 設定失敗：{ex.Message}");
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  ① 程式啟動：建立 WebApidll + 上報 Idle
    // ═══════════════════════════════════════════════════════════
    public async Task ConnectAsync()
    {
        WriteLog("INFO", $"→ ConnectAsync | BaseUrl={_baseUrl}");
        try
        {
            // ★ 建立 WebApidll 物件
            _api = new global::WebApidll.WebApidll(_baseUrl);
            // ★ 訂閱 DLL 內部 Log
            _api.Logged += (_, e) => WriteLog("DLL", e.Content?.ToString() ?? "");
            // ★ 呼叫 WebApidll.BumperSoftwareProcessIdle() 上報 Idle
            var resp = await _api.BumperSoftwareProcessIdle(_equipmentId);
            LastStatus = resp != null ? MesConnectionStatus.Connected : MesConnectionStatus.Disconnected;
            WriteLog("INFO", $"← ConnectAsync | {LastStatus}");
        }
        catch (Exception ex)
        {
            LastStatus = MesConnectionStatus.Disconnected;
            WriteLog("ERROR", $"ConnectAsync 失敗：{ex.Message}");
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  ② 讀碼成功後：QuerySerialNoData
    // ═══════════════════════════════════════════════════════════
    public async Task<string?> QueryWorkOrderAsync(string equipmentId, string productId)
    {
        if (_api is null) { WriteLog("WARN", "尚未連線，略過 QueryWorkOrder"); return null; }
        WriteLog("INFO", $"→ QuerySerialNoData | ProductId={productId}");
        try
        {
            // ★ 呼叫 WebApidll.QuerySerialNoDataAsync()
            var resp = await _api.QuerySerialNoDataAsync(equipmentId, productId);
            if (resp == null) { LastStatus = MesConnectionStatus.Disconnected; return null; }
            LastStatus = MesConnectionStatus.Connected;
            WriteLog("INFO", $"← QuerySerialNoData OK");
            return resp.ToString();
        }
        catch (Exception ex)
        {
            LastStatus = MesConnectionStatus.Disconnected;
            WriteLog("ERROR", $"QueryWorkOrderAsync 失敗：{ex.Message}");
            return null;
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  ③ 量測完成後：MeasureResultUpload + 匯出 CSV
    // ═══════════════════════════════════════════════════════════
    public async Task UploadResultAsync(
        string equipmentId,
        string productId,
        string recipeId,
        bool pass,
        List<BumperResultsData> bumperResultsList,
        string csvFolderPath,
        string processStartTime = "",
        string processEndTime   = "",
        string duringTime       = "")
    {
        if (_api is null) { WriteLog("WARN", "尚未連線，略過 UploadResult"); return; }
        string judge    = pass ? "Pass" : "Fail";
        string startStr = string.IsNullOrEmpty(processStartTime)
            ? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") : processStartTime;
        string endStr   = string.IsNullOrEmpty(processEndTime)
            ? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") : processEndTime;
        WriteLog("INFO", $"→ MeasureResultUpload | ProductId={productId} | RecipeId={recipeId} | Judge={judge}");
        try
        {
            // ★ 呼叫 WebApidll.MeasureResultUploadAsync()
            var resp = await _api.MeasureResultUploadAsync(
                EquipmentId      : equipmentId,
                ProductId        : productId,
                RecipeId         : recipeId,
                TotalJudge       : judge,
                ProductType      : _productType,
                ProcessStartTime : startStr,
                ProcessEndTime   : endStr,
                BumperResultsData: bumperResultsList,
                FliePath         : csvFolderPath);
            LastStatus = resp != null ? MesConnectionStatus.Connected : MesConnectionStatus.Disconnected;
            WriteLog("INFO", $"← MeasureResultUpload | {LastStatus}");
        }
        catch (Exception ex)
        {
            LastStatus = MesConnectionStatus.Disconnected;
            WriteLog("ERROR", $"UploadResultAsync 失敗：{ex.Message}");
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  ⑤ 開始量測：上報 Run 狀態
    // ═══════════════════════════════════════════════════════════
    public async Task ReportRunAsync()
    {
        if (_api is null) { WriteLog("WARN", "尚未連線，略過 ReportRun"); return; }
        WriteLog("INFO", $"→ BumperSoftwareProcessRun | EquipmentId={_equipmentId}");
        try
        {
            var resp = await _api.BumperSoftwareProcessRun(_equipmentId);
            LastStatus = resp != null ? MesConnectionStatus.Connected : MesConnectionStatus.Disconnected;
            WriteLog("INFO", $"← BumperSoftwareProcessRun | {LastStatus}");
        }
        catch (Exception ex) { WriteLog("ERROR", $"ReportRunAsync 失敗：{ex.Message}"); }
    }

    // ═══════════════════════════════════════════════════════════
    //  ⑥ 發生錯誤：上報 Alarm 狀態
    // ═══════════════════════════════════════════════════════════
    public async Task ReportAlarmAsync()
    {
        if (_api is null) { WriteLog("WARN", "尚未連線，略過 ReportAlarm"); return; }
        WriteLog("INFO", $"→ BumperSoftwareAlarmt | EquipmentId={_equipmentId}");
        try
        {
            var resp = await _api.BumperSoftwareAlarmt(_equipmentId);
            LastStatus = resp != null ? MesConnectionStatus.Connected : MesConnectionStatus.Disconnected;
            WriteLog("INFO", $"← BumperSoftwareAlarmt | {LastStatus}");
        }
        catch (Exception ex) { WriteLog("ERROR", $"ReportAlarmAsync 失敗：{ex.Message}"); }
    }

    // ═══════════════════════════════════════════════════════════
    //  ④ 程式關閉：上報 Disconnected
    // ═══════════════════════════════════════════════════════════
    public async Task DisconnectAsync()
    {
        if (_api is null) return;
        WriteLog("INFO", "→ DisconnectAsync");
        try
        {
            // ★ 呼叫 WebApidll.BumperSoftwareDisconnected()
            await _api.BumperSoftwareDisconnected(_equipmentId);
            WriteLog("INFO", "← DisconnectAsync 完成");
        }
        catch (Exception ex) { WriteLog("ERROR", $"DisconnectAsync 失敗：{ex.Message}"); }
    }

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
            File.AppendAllText(path, line + Environment.NewLine, System.Text.Encoding.UTF8);
            System.Diagnostics.Debug.WriteLine($"[CIM] {line}");
        }
        catch { }
    }
}

/// <summary>MES 連線狀態列舉。</summary>
public enum MesConnectionStatus
{
    Unknown,      // 尚未嘗試連線
    Connected,    // 上一次呼叫成功
    Disconnected  // 上一次呼叫失敗
}

/// <summary>CimApiClient 持久化設定。</summary>
internal sealed class CimApiSettings
{
    public string BaseUrl     { get; set; } = "http://192.168.61.28:8069";
    public string EquipmentId { get; set; } = "SlotInspection";
    public string ProductType { get; set; } = "SlotInspection";
    public string ItemName    { get; set; } = "Height";
}
