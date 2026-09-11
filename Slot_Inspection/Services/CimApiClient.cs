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

    private string _productType = "";

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

    private bool _isEnabled = true;

    /// <summary>是否啟用 MES WebAPI 功能。false 時所有 API 呼叫均被略過。</summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set => _isEnabled = value;
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
            var data = new CimApiSettings { BaseUrl = _baseUrl, EquipmentId = _equipmentId, ProductType = _productType, ItemName = _itemName, IsEnabled = _isEnabled };
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
            _isEnabled = data.IsEnabled;
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
        if (!_isEnabled)
        {
            LastStatus = MesConnectionStatus.Disabled;
            WriteLog("INFO", "ConnectAsync 略過（WebAPI 功能已停用）");
            return;
        }
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
    /// <summary>
    /// 查詢 MES 序號相關資料。null＝完全沒打成（功能關閉／尚未連線／連線例外），
    /// 非 null 但 Success=false＝MES 有回應但回錯誤（查 ErrorCode/ErrorMessage）。
    /// </summary>
    public async Task<QuerySerialNoResult?> QueryWorkOrderAsync(string equipmentId, string productId)
    {
        if (!_isEnabled) { LastStatus = MesConnectionStatus.Disabled; return null; }
        if (_api is null) { WriteLog("WARN", "尚未連線，略過 QueryWorkOrder"); return null; }
        WriteLog("INFO", $"→ QuerySerialNoData | ProductId={productId}");
        try
        {
            // ★ 呼叫 WebApidll.QuerySerialNoDataAsync()（已回傳解析好的物件，不用自己剖 JSON）
            var resp = await _api.QuerySerialNoDataAsync(equipmentId, productId);
            if (resp == null) { LastStatus = MesConnectionStatus.Disconnected; return null; }
            LastStatus = MesConnectionStatus.Connected;
            WriteLog("INFO", $"← QuerySerialNoData OK");

            var msg = resp.ReturnMessage;
            var data = resp.QueryData;
            return new QuerySerialNoResult
            {
                Success       = msg?.Code == "0",
                MaterialNo    = data?.MaterialNo,
                WorkerOrderNo = data?.WorkerOrderNo,
                LotNo         = data?.LotNo,
                RfidLotNo     = data?.RfidLotNo,
                ErrorCode     = msg?.Code,
                ErrorMessage  = msg?.Message,
            };
        }
        catch (Exception ex)
        {
            LastStatus = MesConnectionStatus.Disconnected;
            WriteLog("ERROR", $"QueryWorkOrderAsync 失敗：{ex.Message}");
            return null;
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  ③ 量測完成後：MeasureResultUpload
    // ═══════════════════════════════════════════════════════════
    public async Task UploadResultAsync(
        string equipmentId,
        string productId,
        string recipeId,
        bool pass,
        List<BumperResultsData> bumperResultsList,
        string processStartTime = "",
        string processEndTime   = "")
    {
        if (!_isEnabled) { LastStatus = MesConnectionStatus.Disabled; return; }
        if (_api is null) { WriteLog("WARN", "尚未連線，略過 UploadResult"); return; }
        string judge    = pass ? "Pass" : "Fail";
        string startStr = string.IsNullOrEmpty(processStartTime)
            ? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") : processStartTime;
        string endStr   = string.IsNullOrEmpty(processEndTime)
            ? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") : processEndTime;
        // IsItemName：ItemName 欄位是否填入，設定有值時為 true
        bool isItemName = !string.IsNullOrWhiteSpace(_itemName);
        WriteLog("INFO", $"→ MeasureResultUpload | ProductId={productId} | RecipeId={recipeId} | Judge={judge} | IsItemName={isItemName}");
        try
        {
            // ★ 呼叫 WebApidll.MeasureResultUploadAsync()（新 DLL：最後一個參數改為 bool IsItemName）
            var resp = await _api.MeasureResultUploadAsync(
                EquipmentId      : equipmentId,
                ProductId        : productId,
                RecipeId         : recipeId,
                TotalJudge       : judge,
                ProductType      : _productType,
                ProcessStartTime : startStr,
                ProcessEndTime   : endStr,
                BumperResultsData: bumperResultsList,
                IsItemName       : isItemName);
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
        if (!_isEnabled) { LastStatus = MesConnectionStatus.Disabled; return; }
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
        if (!_isEnabled) { LastStatus = MesConnectionStatus.Disabled; return; }
        if (_api is null) { WriteLog("WARN", "尚未連線，略過 ReportAlarm"); return; }
        WriteLog("INFO", $"→ BumperSoftwareAlarmt | EquipmentId={_equipmentId}");
        try
        {
            var resp = await _api.BumperSoftwareAlarm(_equipmentId);
            LastStatus = resp != null ? MesConnectionStatus.Connected : MesConnectionStatus.Disconnected;
            WriteLog("INFO", $"← BumperSoftwareAlarmt | {LastStatus}");
        }
        catch (Exception ex) { WriteLog("ERROR", $"ReportAlarmAsync 失敗：{ex.Message}"); }
    }

    // ═══════════════════════════════════════════════════════════
    //  ⑦ 量測完成：上報 Idle 狀態
    // ═══════════════════════════════════════════════════════════
    public async Task ReportIdleAsync()
    {
        if (!_isEnabled) { LastStatus = MesConnectionStatus.Disabled; return; }
        if (_api is null) { WriteLog("WARN", "尚未連線，略過 ReportIdle"); return; }
        WriteLog("INFO", $"→ BumperSoftwareProcessIdle | EquipmentId={_equipmentId}");
        try
        {
            var resp = await _api.BumperSoftwareProcessIdle(_equipmentId);
            LastStatus = resp != null ? MesConnectionStatus.Connected : MesConnectionStatus.Disconnected;
            WriteLog("INFO", $"← BumperSoftwareProcessIdle | {LastStatus}");
        }
        catch (Exception ex) { WriteLog("ERROR", $"ReportIdleAsync 失敗：{ex.Message}"); }
    }
    //  ④ 程式關閉：上報 Disconnected
    // ═══════════════════════════════════════════════════════════
    public async Task DisconnectAsync()
    {
        if (!_isEnabled) return;
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
    Disconnected, // 上一次呼叫失敗
    Disabled      // WebAPI 功能已關閉
}

/// <summary>
/// CimApiClient.QueryWorkOrderAsync 的解析結果。
/// Success=false 時 MaterialNo 等欄位通常是 null，改看 ErrorCode/ErrorMessage。
/// </summary>
public sealed class QuerySerialNoResult
{
    public bool Success { get; init; }
    public string? MaterialNo { get; init; }
    public string? WorkerOrderNo { get; init; }
    public string? LotNo { get; init; }
    public string? RfidLotNo { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
}

/// <summary>CimApiClient 持久化設定。</summary>
internal sealed class CimApiSettings
{
    public string BaseUrl     { get; set; } = "http://192.168.61.28:8069";
    public string EquipmentId { get; set; } = "SlotInspection";
    public string ProductType { get; set; } = "";
    public string ItemName    { get; set; } = "Height";
    public bool   IsEnabled   { get; set; } = true;
}
