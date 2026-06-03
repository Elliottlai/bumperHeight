using JsonType;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Controls;

namespace WebApidll
{
    #region
    //public class connection
    //{
    //    private readonly HttpClient _httpClient;
    //    public connection(HttpClient httpClient)
    //    {
    //        _httpClient = httpClient;
    //        _httpClient = new HttpClient
    //        {
    //            BaseAddress = new Uri("http://127.0.0.1:8000")
    //        };
    //    }

    //    #region --QuerySerialNoData--
    //    public async Task<string> QuerySerialNoDataResponse(QuerySerialNoDataRequest jsondata, CancellationToken ct = default)
    //    {

    //        var json = JsonSerializer.Serialize(jsondata, new JsonSerializerOptions
    //        {
    //            WriteIndented = true
    //        });
    //        var content = new StringContent(json, Encoding.UTF8, "application/json");
    //        var response = await _httpClient.PostAsJsonAsync("/api/CIMapi/QuerySerialNoData", content);
    //        var respText = await response.Content.ReadAsStringAsync();
    //        return respText;
    //    }
    //    public async Task<string> QuerySerialNoData(CancellationToken ct = default)
    //    {
    //        var response = await _httpClient.GetAsync("/api/CIMapi/QuerySerialNoData", ct);
    //        response.EnsureSuccessStatusCode();

    //        var content = await response.Content.ReadAsStringAsync(ct);
    //        return content;
    //    }

    //    public async Task<connection?> QuerySerialNoDataResponse(
    //        connection response,
    //        CancellationToken ct = default)
    //    {
    //        var responsed = await _httpClient.PostAsJsonAsync("/api/CIMapi/QuerySerialNoData", response, ct);
    //        responsed.EnsureSuccessStatusCode();

    //        return await responsed.Content.ReadFromJsonAsync<connection>(cancellationToken: ct);
    //    }
    //    #endregion
    //    #region --MeasureResultUpload--
    //    public async Task<string> MeasureResultUploadResponse(CancellationToken ct = default)
    //    {
    //        var response = await _httpClient.GetAsync("/api/CIMapi/MeasureResultUpload", ct);
    //        response.EnsureSuccessStatusCode();

    //        var content = await response.Content.ReadAsStringAsync(ct);
    //        return content;
    //    }

    //    public async Task<connection?> MeasureResultUploadPost(
    //        connection response,
    //        CancellationToken ct = default)
    //    {
    //        var responsed = await _httpClient.PostAsJsonAsync("/api/CIMapi/QuerySerialNoData", response, ct);
    //        responsed.EnsureSuccessStatusCode();

    //        return await responsed.Content.ReadFromJsonAsync<connection>(cancellationToken: ct);
    //    }

    //    #endregion
    //    #region --EqStatusReport--
    //    public async Task<string> EqStatusReportResponse(CancellationToken ct = default)
    //    {
    //        var response = await _httpClient.GetAsync("/api/CIMapi/EqStatusReport", ct);
    //        response.EnsureSuccessStatusCode();

    //        //var json = JsonSerializer.Serialize(dataToInsert, new JsonSerializerOptions
    //        //{
    //        //    WriteIndented = true
    //        //});
    //        //var content = new StringContent(json, Encoding.UTF8, "application/json");
    //        //var response = await httpClient.PostAsync("http://127.0.0.1:8000/documents/upsert", content);
    //        //var respText = await response.Content.ReadAsStringAsync();
    //        //
    //        //Debug.WriteLine(response.StatusCode);
    //        //Debug.WriteLine(respText);


    //        var content = await response.Content.ReadAsStringAsync(ct);
    //        return content;
    //    }

    //    public async Task<connection?> EqStatusPost(
    //        connection response,
    //        CancellationToken ct = default)
    //    {
    //        var responsed = await _httpClient.PostAsJsonAsync("/api/CIMapi/QuerySerialNoData", response, ct);
    //        responsed.EnsureSuccessStatusCode();

    //        return await responsed.Content.ReadFromJsonAsync<connection>(cancellationToken: ct);
    //    }
    //    #endregion
    //}
    //#region --QuerySerialNoDataList-
    //public class QuerySerialNoDataRequest
    //{
    //    public string EventType { get; set; }
    //    public string RecordId { get; set; }
    //    public string RecordDateTime { get; set; }
    //    public string EquipmentId { get; set; }
    //    public string ProductId { get; set; }
    //}
    //public class QuerySerialNoDataResponse
    //{
    //    public string EventType { get; set; }
    //    public string RecordId { get; set; }
    //    public string RecordDateTime { get; set; }
    //    public string EquipmentId { get; set; }
    //    public string ProductId { get; set; }
    //    public QueryData queryData { get; set; }
    //    public ReturnMessage returnMessage { get; set; }    
    //}
    //public class QueryData
    //{
    //    public string MaterialNo { get; set; }
    //    public string WorkerOrderNo { get; set; }
    //    public string LotNo { get; set; }
    //    public string RfidLotNo { get; set; }
    //    public string RecipeId { get; set; }
    //}
    //#endregion
    //#region --MeasureResultUpload--
    //public class MeasureResultUploadRequest
    //{
    //    public string EventType { get; set; }
    //    public string RecordId { get; set; }
    //    public string RecordDateTime { get; set; }
    //    public string EquipmentId { get; set; }
    //    public string ProductId { get; set; }
    //    public string RecipeId { get; set; }
    //    public string TotalJudge { get; set; }
    //    public string ProductType { get; set; }
    //    public string ProcessStartTime { get; set; }
    //    public string ProcessEndTime { get; set; }
    //    public List<MeasureResult> MeasureResultList { get; set; }
    //}
    //public class MeasureResultUploadResponse
    //{
    //    public string EventType { get; set; }
    //    public string RecordId { get; set; }
    //    public string RecordDateTime { get; set; }
    //    public string EquipmentId { get; set; }
    //    public string ProductId { get; set; }
    //    public string RecipeId { get; set; }
    //    public string TotalJudge { get; set; }
    //    public string ProductType { get; set; }
    //    public string ProcessStartTime { get; set; }
    //    public string ProcessEndTime { get; set; }
    //    public List<MeasureResult> MeasureResultList { get; set; }
    //    public ReturnMessage returnMessage { get; set; }
    //}
    //public class MeasureResult
    //{
    //    public string DataId { get; set; }
    //    public string ItemName { get; set; }
    //    public string ItemValue { get; set; }
    //    public string UpperSpecLimit { get; set; }
    //    public string LowerSpecLimit { get; set; }
    //    public string ItemJudge { get; set; }
    //    public string ProcessStartTime { get; set; }
    //    public string ProcessEndTime { get; set; }
    //    public string DuringTime { get; set; }
    //}
    //#endregion
    //#region --EqStatusReport--
    //public class EqStatusReportRequest
    //{
	   // public string EventType { get; set; }
    //    public string RecordId { get; set; }
    //    public string RecordDateTime { get; set; }
    //    public string EquipmentId { get; set; }
    //    public string EquipmentStatus { get; set; }
    //}
    //public class EqStatusReportResponse
    //{
    //    public string EventType { get; set; }
    //    public string RecordId { get; set; }
    //    public string RecordDateTime { get; set; }
    //    public string EquipmentId { get; set; }
    //    public string EquipmentStatus { get; set; }
    //    public ReturnMessage returnMessage { get; set; }
    //}
    //#endregion
    //public class ReturnMessage
    //{
    //    public string Code { get; set; }
    //    public string Message { get; set; } 
    //}
    #endregion

    #region
    public class WebApidll
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl = "http://192.168.61.28:8069";
        public event EventHandler<LogEventArgs> Logged;
        public int recordId = 0;
        /// <summary>
        /// WebApidll
        /// </summary>
        /// <param name="_baseUrl">請輸入網址或者設定檔位置</param>
        /// <exception cref="InvalidOperationException"></exception>
        /// <exception cref="ArgumentException"></exception>
        public WebApidll(string _baseUrl)
        {
            // 判斷傳入的是檔案路徑還是 HTTP 連結
            string resolvedUrl;
            if (_baseUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                _baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                // 直接是 HTTP/HTTPS 連結
                resolvedUrl = _baseUrl;
            }
            else if (File.Exists(_baseUrl))
            {
                // 是檔案路徑，讀取第一行作為 URL
                resolvedUrl = File.ReadLines(_baseUrl).FirstOrDefault()?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(resolvedUrl))
                    throw new InvalidOperationException($"檔案 '{_baseUrl}' 內容為空，無法取得 Base URL。");
            }
            else
            {
                throw new ArgumentException($"無法識別的 Base URL 或找不到檔案：'{_baseUrl}'");
            }

            _httpClient = new HttpClient
            {
                BaseAddress = new Uri(resolvedUrl)
            };
            _httpClient.DefaultRequestHeaders.Accept.Add(
                new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        }
        //private void Log(string message)
        //    => Logged?.Invoke(this, new LogEventArgs(message));

        private void Log(string message)
        {
            var logEvent = new LogEventArgs(message);
            // 1. 觸發事件 → UI 界面顯示 LOG（保留原有行為）
            Logged?.Invoke(this, logEvent);
            // 2. 同時寫入外部 txt 檔案
            Logger.General.Log(logEvent);
        }
        #region ====================== QuerySerialNoData ===================

        /// <summary>
        /// QuerySerialNoData Api呼叫(詢問MES Server相關資訊)
        /// </summary>
        /// <param name="equipmentId">設備ID</param>
        /// <param name="productId">產品序號</param>
        /// <returns>equipmentId = 設備ID, productId = 產品序號</returns>
        public async Task<QuerySerialNoDataResponse> QuerySerialNoDataAsync(
            string equipmentId , string productId)
        {
            if (recordId == 9999)
                recordId = 1;
            else
                recordId++;

            var requestModel = new QuerySerialNoDataRequest
            {
                EventType = "QuerySerialNoData",
                RecordId = recordId.ToString("D4"),
                RecordDateTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                EquipmentId = equipmentId,
                ProductId = productId
            };

            string jsonRequest = JsonSerializer.Serialize(requestModel, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            Log($"QuerySerialNoData Request: {jsonRequest}");

            var content = new StringContent(jsonRequest, Encoding.UTF8, "application/json");

            try
            {
                HttpResponseMessage response = await _httpClient.PostAsync("/api/CIMapi/QuerySerialNoData", content);
                string responseBody = await response.Content.ReadAsStringAsync();

                Log($"QuerySerialNoDataResponseBody: {responseBody}");

                if (response.IsSuccessStatusCode)
                {
                    var result = JsonSerializer.Deserialize<QuerySerialNoDataResponse>(responseBody);
                    Log($"QuerySerialNoDataResponse: {result}");
                    return result;
                }
                else
                {
                    Log($"HTTP Error: {response.StatusCode}");

                    return null;
                }
            }
            catch (Exception ex)
            {
                Log($"Exception: {ex.Message}");
                return null;
            }
        }
        #endregion

        #region ====================== EqStatusReport ======================
        /// <summary>
        /// 機台狀態上報 API 呼叫
        /// </summary>
        /// <param name="EquipmentId">設備ID名稱</param>
        /// <param name="EquipmentStatus">機台狀態</param>
        /// <returns></returns>
        private async Task<EqStatusReportResponse> EqStatusReportAsync(string EquipmentId,string EquipmentStatus)
        {
            if (recordId == 9999)
                recordId = 1;
            else
                recordId++;
            var request = new EqStatusReportRequest
            {
                EventType = "EqStatusReport",
                RecordId = recordId.ToString("D4"),
                RecordDateTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                EquipmentId = EquipmentId,
                EquipmentStatus = EquipmentStatus
            };

            string jsonRequest = JsonSerializer.Serialize(request, new JsonSerializerOptions { WriteIndented = true });

            Log($"EqStatusReportJsonRequest: {jsonRequest}");

            var content = new StringContent(jsonRequest, Encoding.UTF8, "application/json");

            try
            {
                var response = await _httpClient.PostAsync("/api/CIMapi/EqStatusReport", content);
                string responseBody = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<EqStatusReportResponse>(responseBody);
                Log($"EqStatusReportResponseBody: {responseBody}");

                if (response.IsSuccessStatusCode)
                {
                    Log($"EqStatusResponse SuccesJson: {result}");
                    return result;
                }
                else
                {
                    Log($"EqStatusResponse ErrMessage : {result.ReturnMessage.Message} ");
                    return result;
                }                 
            }
            catch (Exception ex)
            {
                Log($"EqStatusReport Exception: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 模擬設備啟動，並上報 Idle 狀態
        /// </summary>
        /// <param name="EquipmentId">設備ID</param>
        /// <returns></returns>
        public async Task<EqStatusReportResponse> BumperSoftwareProcessIdle(string EquipmentId)
        {
            EqStatusReportResponse Response = await EqStatusReportAsync(EquipmentId, "Idle");
            return Response;
        }

        /// <summary>
        /// 模擬設備運行中，並上報 Run 狀態
        /// </summary>
        /// <param name="EquipmentId">設備ID</param>
        /// <returns></returns>
        public async Task<EqStatusReportResponse> BumperSoftwareProcessRun(string EquipmentId)
        {
            EqStatusReportResponse Response = await EqStatusReportAsync(EquipmentId, "Run");
            return Response;
        }

        /// <summary>
        /// 模擬設備閒置，並上報 Alarm 狀態
        /// </summary>
        /// <param name="EquipmentId">設備ID</param>
        /// <returns></returns>
        public async Task<EqStatusReportResponse> BumperSoftwareAlarm(string EquipmentId)
        {
            EqStatusReportResponse Response = await EqStatusReportAsync(EquipmentId, "Alarm");
            return Response;
        }

        /// <summary>
        /// 模擬設備關機，並上報 Disconnected 狀態
        /// </summary>
        /// <param name="EquipmentId">設備ID</param>
        /// <returns></returns>
        public async Task<EqStatusReportResponse> BumperSoftwareDisconnected(string EquipmentId)
        {
            EqStatusReportResponse Response = await EqStatusReportAsync(EquipmentId, "Disconnected");
            return Response;
        }

        #region --測試--
        #region
        //public async Task EqStatusReportAsync_ByTest(EqStatusReportRequest request)
        //{
        //    string jsonRequest = JsonSerializer.Serialize(request, new JsonSerializerOptions { WriteIndented = true });

        //    Debug.WriteLine("=== EqStatusReport Request ===");
        //    Debug.WriteLine(jsonRequest);
        //    Log($"EqStatusReportJsonRequest: {jsonRequest}");

        //    var content = new StringContent(jsonRequest, Encoding.UTF8, "application/json");

        //    try
        //    {
        //        var response = await _httpClient.PostAsync("/api/CIMapi/EqStatusReport", content);
        //        string responseBody = await response.Content.ReadAsStringAsync();

        //        Debug.WriteLine("\n=== EqStatusReport Response ===");
        //        Debug.WriteLine(responseBody);
        //        Log($"EqStatusReportResponseBody: {responseBody}");

        //        if (!response.IsSuccessStatusCode)
        //        {
        //            Debug.WriteLine($"HTTP Status Code: {response.StatusCode}");
        //            Log($"EqStatusReport HTTP Status Code: {response.StatusCode}");
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        Debug.WriteLine($"Exception: {ex.Message}");
        //        Log($"EqStatusReport Exception: {ex.Message}");
        //    }
        //}
        #endregion
        public async Task EqStatusReportAsync_ByTest(string EquipmentId, string EquipmentStatus)
        {
            if (recordId == 9999)
                recordId = 1;
            else
                recordId++;
            var request = new EqStatusReportRequest
            {
                EventType = "EqStatusReport",
                RecordId = recordId.ToString("D4"),
                RecordDateTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                EquipmentId = EquipmentId,
                EquipmentStatus = EquipmentStatus
            };

            string jsonRequest = JsonSerializer.Serialize(request, new JsonSerializerOptions { WriteIndented = true });

            Log($"EqStatusReportJsonRequest: {jsonRequest}");

            var content = new StringContent(jsonRequest, Encoding.UTF8, "application/json");

            try
            {
                var response = await _httpClient.PostAsync("/api/CIMapi/EqStatusReport", content);
                string responseBody = await response.Content.ReadAsStringAsync();

                Log($"EqStatusReportResponseBody: {responseBody}");

                if (!response.IsSuccessStatusCode)
                {
                    Log($"EqStatusReport HTTP Status Code: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Exception: {ex.Message}");
                Log($"EqStatusReport Exception: {ex.Message}");
            }
        }
        #endregion
        #endregion

        #region ====================== MeasureResultUpload =================

        #region ==ExportToCsv==
        //public static void ExportToCsv<T>(List<T> dataList, string filePath)
        //{
        //    if (dataList == null || dataList.Count == 0) return;

        //    var properties = typeof(T).GetProperties();

        //    var sb = new StringBuilder();

        //    // 寫入標題列
        //    sb.AppendLine(string.Join(",", properties.Select(p => p.Name)));

        //    // 寫入資料列
        //    foreach (var item in dataList)
        //    {
        //        var values = properties.Select(p =>
        //        {
        //            var value = p.GetValue(item)?.ToString() ?? string.Empty;
        //            // 若值包含逗號或換行，用雙引號包覆
        //            if (value.Contains(',') || value.Contains('\n') || value.Contains('"'))
        //                value = $"\"{value.Replace("\"", "\"\"")}\"";
        //            return value;
        //        });
        //        sb.AppendLine(string.Join(",", values));
        //    }

        //    File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        //}
        #endregion

        /// <summary>
        /// 量測結果上傳 API 呼叫
        /// </summary>
        /// <param name="EquipmentId">設備ID</param>
        /// <param name="ProductId">產品序號</param>
        /// <param name="RecipeId">Recipe代碼</param>
        /// <param name="TotalJudge">整體量測結果: Pass/Fail</param>
        /// <param name="ProductType">量測的產品類型</param>
        /// <param name="ProcessStartTime">整體量測起始時間，yyyy-MM-dd HH:mm:ss.fff</param>
        /// <param name="ProcessEndTime">整體量測結束時間，yyyy-MM-dd HH:mm:ss.fff</param>
        /// <param name="BumperResultsData">整體量測結果</param>
        /// <param name="FliePath">CSV存檔位置</param>
        /// <returns></returns>
        //public async Task<MeasureResultUploadResponse> MeasureResultUploadAsync(string EquipmentId,
        //    string ProductId,
        //    string RecipeId,
        //    string TotalJudge,
        //    string ProductType,
        //    string ProcessStartTime,
        //    string ProcessEndTime,
        //    List<BumperResultsData> BumperResultsData,
        //    string FliePath)
        public async Task<MeasureResultUploadResponse> MeasureResultUploadAsync(string EquipmentId,
            string ProductId,
            string RecipeId,
            string TotalJudge,
            string ProductType,
            string ProcessStartTime,
            string ProcessEndTime,
            List<BumperResultsData> BumperResultsData,
            bool IsItemName)
            {
            if(IsItemName)
            {
                #region
                var MeasureResultItemList = MeasureResultItem(BumperResultsData, IsItemName);
                var request = MeasureResultDataList(EquipmentId, ProductId, RecipeId, TotalJudge, ProductType, ProcessStartTime, ProcessEndTime,
                MeasureResultItemList);

                string jsonRequest = JsonSerializer.Serialize(request, new JsonSerializerOptions { WriteIndented = true });
                string jsonFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                    $"MeasureResultUpload_{DateTime.Now:yyyyMMdd_HHmmss}.json");
                File.WriteAllText(jsonFilePath, jsonRequest, Encoding.UTF8);
                Log($"MeasureResultUpload JSON 已儲存至: {jsonFilePath}");

                var content = new StringContent(jsonRequest, Encoding.UTF8, "application/json");

                try
                {
                    var response = await _httpClient.PostAsync("/api/CIMapi/MeasureResultUpload", content);
                    string responseBody = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<MeasureResultUploadResponse>(responseBody);
                    Log($"MeasureResultUploadResponseBody: {responseBody}");
                    if (response.IsSuccessStatusCode)
                    {
                        Log($"MeasureResultUpload HTTP Status Code: {response.StatusCode}");
                        return result;
                    }
                    else
                    {
                        Log($"MeasureResultResponse ErrMessage: {result.ReturnMessage.Message}");
                        return result;
                    }
                }
                catch (Exception ex)
                {
                    Log($"MeasureResultUpload Exception: {ex.Message}");
                    return null;
                }
                #endregion
            }
            else
            {
                #region
                var MeasureResultItemList = MeasureResultItem(BumperResultsData, IsItemName);
                var request = MeasureResultDataList(EquipmentId, ProductId, RecipeId, TotalJudge, ProductType, ProcessStartTime, ProcessEndTime,
                MeasureResultItemList);

                string jsonRequest = JsonSerializer.Serialize(request, new JsonSerializerOptions { WriteIndented = true });
                string jsonFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                    $"MeasureResultUpload_{DateTime.Now:yyyyMMdd_HHmmss}.json");
                File.WriteAllText(jsonFilePath, jsonRequest, Encoding.UTF8);
                Log($"MeasureResultUpload JSON 已儲存至: {jsonFilePath}");

                var content = new StringContent(jsonRequest, Encoding.UTF8, "application/json");

                try
                {
                    var response = await _httpClient.PostAsync("/api/CIMapi/MeasureResultUpload", content);
                    string responseBody = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<MeasureResultUploadResponse>(responseBody);
                    Log($"MeasureResultUploadResponseBody: {responseBody}");
                    if (response.IsSuccessStatusCode)
                    {
                        Log($"MeasureResultUpload HTTP Status Code: {response.StatusCode}");
                        return result;
                    }
                    else
                    {
                        Log($"MeasureResultResponse ErrMessage: {result.ReturnMessage.Message}");
                        return result;
                    }
                }
                catch (Exception ex)
                {
                    Log($"MeasureResultUpload Exception: {ex.Message}");
                    return null;
                }
                #endregion
            }
            #region ── CSV 路徑判斷 ──────────────────────────────────────────
            //string autoFileName = $"MeasureResult_{DateTime.Now:yyyyMMddHHmmss}.csv";
            //string resolvedCsvPath;

            //if (string.IsNullOrWhiteSpace(FliePath))
            //{
            //    // 未填寫：存至執行檔所在目錄
            //    resolvedCsvPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, autoFileName);
            //    Log($"CSV 路徑未指定，自動使用預設路徑: {resolvedCsvPath}");
            //}
            //else
            //{
            //    try
            //    {
            //        // 無論使用者是否填入檔名，一律取目錄部分並套用自動命名檔名
            //        string dir;

            //        if (string.IsNullOrWhiteSpace(Path.GetExtension(FliePath)))
            //        {
            //            // 使用者填的是純目錄（無副檔名）
            //            dir = FliePath;
            //        }
            //        else
            //        {
            //            // 使用者填了含檔名的路徑，取出目錄部分
            //            dir = Path.GetDirectoryName(FliePath) ?? AppDomain.CurrentDomain.BaseDirectory;
            //        }

            //        // 目錄不存在 → 自動建立（使用者有明確指定路徑時才建立）
            //        if (!Directory.Exists(dir))
            //        {
            //            Directory.CreateDirectory(dir);
            //            Log($"CSV 目錄不存在，已自動建立: {dir}");
            //        }

            //        // 一律使用自動時間命名，忽略使用者填入的原始檔名
            //        resolvedCsvPath = Path.Combine(dir, autoFileName);
            //        Log($"CSV 將儲存至: {resolvedCsvPath}");
            //    }
            //    catch (Exception ex)
            //    {
            //        resolvedCsvPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, autoFileName);
            //        Log($"CSV 路徑處理失敗 ({ex.Message})，自動使用預設路徑: {resolvedCsvPath}");
            //    }
            //}
            #endregion ─────────────────────────────────────────────────────────
            //var MeasureResultItemList = MeasureResultItem(BumperResultsData, resolvedCsvPath);
        }

        /// <summary>
        /// 檢測結果資料轉換成 MeasureResultItem List 並匯出成 CSV 檔案
        /// </summary>
        /// <param name="BumperResultsData">檢測結果資料</param>
        /// <param name="CSVFilePath">CSV存檔路徑</param>
        /// <returns></returns>
        public List<MeasureResultItem> MeasureResultItem(List<BumperResultsData> BumperResultsData,bool ItemName)
        //public List<MeasureResultItem> MeasureResultItem(List<BumperResultsData> BumperResultsData, string CSVFilePath)
        {
            //ExportToCsv(BumperResultsData, CSVFilePath);
            if(ItemName)
            {
                #region
                List<MeasureResultItem> MeasureResultList = new List<MeasureResultItem>();
                int dataId = 0;
                foreach (var result in BumperResultsData)
                {
                    dataId++;
                    MeasureResultItem item = new MeasureResultItem
                    {
                        DataId = dataId.ToString(),
                        ItemName = result.ItemName,
                        ItemValue = result.ItemValue,
                        UpperSpecLimit = result.UpperSpecLimit,
                        LowerSpecLimit = result.LowerSpecLimit,
                        ItemJudge = result.ItemJudge,
                        ProcessStartTime = result.ProcessStartTime,
                        ProcessEndTime = result.ProcessEndTime,
                        DuringTime = result.DuringTime
                    };
                    MeasureResultList.Add(item);
                }
                return MeasureResultList;
                #endregion
            }
            else
            {
                #region
                List<MeasureResultItem> MeasureResultList = new List<MeasureResultItem>();
                int dataId = 0;
                foreach (var result in BumperResultsData)
                {
                    dataId++;
                    MeasureResultItem item = new MeasureResultItem
                    {
                        DataId = dataId.ToString(),
                        ItemName = "Slot#" + result.Layer + "_" + result.BumperPosition,
                        ItemValue = result.ItemValue,
                        UpperSpecLimit = result.UpperSpecLimit,
                        LowerSpecLimit = result.LowerSpecLimit,
                        ItemJudge = result.ItemJudge,
                        ProcessStartTime = result.ProcessStartTime,
                        ProcessEndTime = result.ProcessEndTime,
                        DuringTime = result.DuringTime
                    };
                    MeasureResultList.Add(item);
                }
                return MeasureResultList;
                #endregion
            }

        }

        /// <summary>
        /// 完整的MeasureResultUpload json資料組成，包含整體量測資訊與量測項目結果集合
        /// </summary>
        /// <param name="EquipmentId">設備ID</param>
        /// <param name="ProductId">產品序號</param>
        /// <param name="RecipeId">Recipe代碼</param>
        /// <param name="TotalJudge">整體量測結果: Pass/Fail</param>
        /// <param name="ProductType">量測的產品類型</param>
        /// <param name="ProcessStartTime">整體量測起始時間，yyyy-MM-dd HH:mm:ss.fff</param>
        /// <param name="ProcessEndTime">整體量測結束時間，yyyy-MM-dd HH:mm:ss.fff</param>
        /// <param name="MeasureResultItemList">量測結果集合</param>
        /// <returns></returns>
        public MeasureResultUploadRequest MeasureResultDataList(
            string EquipmentId,
            string ProductId,
            string RecipeId,
            string TotalJudge,
            string ProductType,
            string ProcessStartTime,
            string ProcessEndTime,
            List<MeasureResultItem> MeasureResultItemList)
        {
            if (recordId == 9999)
                recordId = 1;
            else
                recordId++;

            MeasureResultUploadRequest MeasureResultUploadRequest = new MeasureResultUploadRequest
            {
                RecordId = recordId.ToString("D4"),
                EquipmentId = EquipmentId,
                ProductId = ProductId,
                RecipeId = RecipeId,
                TotalJudge = TotalJudge,
                ProductType = ProductType,
                ProcessStartTime = ProcessStartTime,
                ProcessEndTime = ProcessEndTime,
                MeasureResultList = MeasureResultItemList
            };
            return MeasureResultUploadRequest;
        }
        #endregion
    }
    #endregion
}
