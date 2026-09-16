namespace Slot_Inspection.Models;

using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>單一相機的裁切 ROI 設定。-1 表示不裁切。</summary>
public sealed class CameraRoi
{
    public string CameraId { get; set; } = "";
    public int X { get; set; } = -1;
    public int Y { get; set; } = -1;
    public int W { get; set; } = -1;
    public int H { get; set; } = -1;

    /// <summary>四個值都有效才啟用裁切。</summary>
    public bool IsEnabled => X >= 0 && Y >= 0 && W > 0 && H > 0;
}

/// <summary>
/// Inspection parameters (light, exposure, NG threshold).
/// </summary>
public sealed class InspectionConfig
{
    // -- Light (Left camera, CH1) --
    public int LightChannelLeft { get; set; } = 1;
    public int LightIntensityLeft { get; set; } = 50;

    // -- Light (Right camera, CH2) --
    public int LightChannelRight { get; set; } = 2;
    public int LightIntensityRight { get; set; } = 50;

    public int LightStabilizeMs { get; set; } = 50;

    // -- Camera --
    public int CaptureWaitMs { get; set; } = 100;
    public bool SaveImages { get; set; } = true;
    public string ImageSavePath { get; set; } = @"D:\InspectionImages";

    /// <summary>
    /// 每個 Slot「拍照＋分析」步驟整段至少要花多久（ms），從進入拍照步驟開始計時
    /// （光源重設 → 穩定 → 取像 → 存檔 → AI 分析 → 畫線存檔），實際處理已經比這個值長時
    /// 不會額外等待，比較短時才補滿到這個下限。可在機台上實測調降、評估對週期時間的影響
    /// （見 RunTimingRecorder 的 [Timing] 統計）。
    /// 注意：2026-09-15 修正前的版本這裡有 bug，補滿時只扣掉固定的 400ms（光源穩定 100 + 取像等待 300），
    /// 沒扣掉實際已花的時間，所以每個 Slot 永遠多等「下限 − 400ms」（預設 2000 時就是固定多等 1600ms）。
    /// 這段等待發生在 Z 軸已停止、還沒開始上升之間，調整它不會改變馬達動作順序。
    /// </summary>
    public int CaptureMinDurationMs { get; set; } = 2000;

    /// <summary>
    /// 週期時間比較基準（秒/Slot）。RUN 結束的 [Timing] 摘要會把本次平均每 Slot 秒數跟這個值比較，
    /// 顯示快/慢多少秒與百分比。預設 6.63 為 2026-09-15 機台 15 次完整 RUN（25 Slot）的實測平均（優化前）。
    /// &lt;= 0 表示不做比較。不顯示在 UI，只能改設定檔。
    /// </summary>
    public double CycleTimeBaselineSecPerSlot { get; set; } = 6.63;

    /// <summary>
    /// 客戶規定的整批 RUN 週期時間（秒）。RUN 結束的 [Timing] 摘要會判定本次是否超過。
    /// &lt;= 0 表示未設定、不判定。不顯示在 UI，只能改設定檔。
    /// 2026-09-16：客戶明確要求 120 秒，設為預設值，讓機台上每次 RUN 都自動判定有沒有超過。
    /// </summary>
    public double CycleTimeTargetSec { get; set; } = 120;

    /// <summary>右相機曝光時間 (µs)。原本寫死在 MachineController.cs，現改由設定檔控制。</summary>
    public double ExposureTimeRight { get; set; } = 36500.0;

    /// <summary>左相機曝光時間 (µs)。原本寫死在 MachineController.cs，現改由設定檔控制。</summary>
    public double ExposureTimeLeft { get; set; } = 36500.0;

    // -- Axis settle delay (防止 ZL/ZR 到位後振動拍照模糊) --
    /// <summary>
    /// 是否在 ZL/ZR 軸到位後，確保至少經過 <see cref="AxisSettleDelayMs"/> ms 才開始曝光，
    /// 以避免機械振動造成影像模糊。可透過 UI CheckBox 關閉。
    /// RUN 流程（DryRunAsync）從 2026-09-15 起也套用這個設定：計時從 ZL/ZR 到位確認開始，
    /// 中間已經花掉的時間（光閘檢查、光源重設等）會扣掉，不夠才補等。
    /// 注意：在這之前，這個設定雖然畫面上一直有「⏱ 安定延遲」勾選框可以切換，但實際只接在
    /// `InspectOneSlotAsync`（OnStart() 底下的死程式碼，沒有按鈕會呼叫到）上，對真正的 RUN
    /// 完全沒有作用；這次是第一次真的接上 RUN。預設先關閉，原因：①過去沒有這段等待，拍照
    /// 也沒有反應過模糊問題，直接開等於是無憑無據多花時間；②客戶要求整批 120 秒，開啟後
    /// 每 Slot 多花最多 150ms（25 Slot 約 3~4 秒），會直接壓縮到達成 120 秒目標的安全邊界。
    /// 之後機台上如果真的觀察到影像模糊、量測結果不穩，再打開來換取穩定度。
    /// </summary>
    public bool EnableAxisSettleDelay { get; set; } = false;

    /// <summary>
    /// ZL/ZR 到位後等待振動平息的時間 (ms)，預設 150 ms。
    /// <see cref="EnableAxisSettleDelay"/> 為 false 時不生效。
    /// 若 RUN 拍出的影像有模糊/位置偏移，優先調高這個值。
    /// </summary>
    public int AxisSettleDelayMs { get; set; } = 150;

    // =========================================
    //  RUN 週期時間優化開關（不上 UI，只能改設定檔；都不會改變馬達動作順序）
    //  每個開關都可以單獨切回舊行為，機台上發現影像/結果異常時不用重新編譯。
    // =========================================

    /// <summary>
    /// true = 每個 Slot 拍照前都重新設定一次光源亮度（舊行為，每個 Slot 多花約 300ms 以上，
    /// 因為光源控制器兩個指令之間至少要隔 300ms）。
    /// false（預設）= 只在 RUN 開始時開燈一次；若 RUN 開始時開燈失敗，才改回每個 Slot 重設。
    /// RUN 過程中程式不會主動關燈（暫停也不會），只有 RUN 結束才關。
    /// </summary>
    public bool LightReassertEachSlot { get; set; } = false;

    /// <summary>
    /// true（預設）= 取像時一收到相機「整張影像傳完」事件就停止等待（最多等 300ms，跟舊行為一樣）；
    /// false = 固定等 300ms（舊行為）。影像若出現殘缺/上一片的畫面，改回 false。
    /// </summary>
    public bool CaptureWaitForFrameEvent { get; set; } = true;

    /// <summary>
    /// true = 左右兩張影像的 AI 分析同時進行；false（預設）= 先右後左依序分析（舊行為）。
    /// 預設關閉：AI 元件（廠商 BumperFlatDetection）是否允許兩張同時分析尚未驗證，
    /// 開啟前要在機台上確認同一片板子開/關兩種模式的量測數值一致、程式不會當掉。
    /// </summary>
    public bool ParallelAiAnalysis { get; set; } = false;

    /// <summary>
    /// true（預設）= 畫線結果圖（_draw.bmp）改到背景存檔，不佔 Z 軸停留時間；RUN 結束前會等全部存完。
    /// false = 存完才繼續（舊行為）。原圖一律先存完（AI 分析要讀原圖檔），不受這個設定影響。
    /// </summary>
    public bool SaveDrawImageInBackground { get; set; } = true;

    /// <summary>
    /// RUN 流程中 Y/Z 軸移動後，每隔多久（ms）詢問一次是否到位。舊行為固定 100ms；
    /// 預設 50ms，讓軸到位後更快往下走。調太小會增加 RS485 通訊量，不建議低於 30。
    /// 只影響「多快發現已經到位」，不影響移動前的安全確認與 150ms 等待。
    /// </summary>
    public int RunMovePollIntervalMs { get; set; } = 50;

    /// <summary>
    /// true（預設）= 初始化完成時先把 AI 模型載入記憶體（初始化多花約 3 秒），
    /// 避免程式重開後第一次 RUN 的第一片要多等 2~3 秒載入模型。只在 <see cref="UseNewBumperAlgorithm"/>=true 時生效。
    /// </summary>
    public bool PreloadAiModelOnInit { get; set; } = true;

    // =========================================
    //  板子感測（X3/X11）個別停用設定（Admin 限定）
    //  與 MainViewModel 的「暫停板子感測」快捷按鈕（同時忽略 X3+X11、不持久化）並存，
    //  這裡是可分開停用單一訊號、寫入設定檔持久化的細部設定。
    // =========================================

    /// <summary>是否停用 X3 板子放置感測判斷（停用後該訊號永遠視為正常）。</summary>
    public bool PauseX3BoardSensor { get; set; } = false;

    /// <summary>是否停用 X11 板子放置感測判斷（停用後該訊號永遠視為正常）。</summary>
    public bool PauseX11BoardSensor { get; set; } = false;

    /// <summary>
    /// true=使用新版 BumperFlatDetection AI 演算法；false=使用原本的 BumperFlat.ImageProcessing。
    /// 預設 false，評估階段須明確切換才會啟用（模型有授權到期日、現場 GPU 可用性未確認）。
    /// </summary>
    public bool UseNewBumperAlgorithm { get; set; } = false;

    // -- NG threshold --
    public double NgThresholdLow { get; set; } = 0.40;
    public double NgThresholdHigh { get; set; } = 0.60;

    // -- Value limit (mm^2) --
    public double ValueLimitLow  { get; set; } = 0.0;
    public double ValueLimitHigh { get; set; } = 999.0;

    // -- QC 基準比對 --
    /// <summary>是否啟用 QC 比對：關閉時每輪 RUN 後不自動比對、不寫狀態列訊息、不產生 CSV；
    /// 「QC 比對」視窗仍可手動按「重新比對」查看。</summary>
    public bool QcCompareEnabled { get; set; } = true;

    // 與 QC 基準值比較時，任一項差距超過對應允許範圍即標記「偏離基準」
    public double QcToleranceValue { get; set; } = 0.05;
    public double QcToleranceMeanIntensity { get; set; } = 5.0;
    public double QcToleranceDistanceToRoiCenter { get; set; } = 0.1;

    // -- Axis move timeout --
    public TimeSpan MoveTimeout { get; set; } = TimeSpan.FromSeconds(30);

    // -- Simulation images --
    /// <summary>
    /// Test image folder path. Supports *.png / *.jpg / *.bmp / *.tif.
    /// Images are cycled; if folder is empty or missing, colored block images are generated instead.
    /// </summary>
    public string SimImageFolderPath { get; set; } = @"D:\TestImages";

    // =========================================
    //  Barcode 讀碼軸設定（只有 X 軸移動）
    //  TODO: 依實際 teaching 結果調整
    // =========================================

    /// <summary>讀碼時 Y 軸移動目標位置 (mm)</summary>
    public double BarcodePositionY { get; set; } = 491.0;

    /// <summary>掃左側條碼時，X 軸位置</summary>
    public double BarcodePositionLeftX { get; set; } = -5.121;

    /// <summary>掃右側條碼時，X 軸位置</summary>
    public double BarcodePositionRightX { get; set; } = 189.922;

    /// <summary>依載台在席位置回傳讀碼器的 X 軸座標</summary>
    public double GetBarcodePositionX(Services.MachineController.CarrierPosition position)
        => position == Services.MachineController.CarrierPosition.Right
            ? BarcodePositionRightX
            : BarcodePositionLeftX;

    // =========================================
    //  Z 軸高度設定 — R 組（X12=true，板子在右側）
    //  TODO: 依實際 teaching 結果調整
    // =========================================

    /// <summary>R組：ZL 下降後的檢測高度（mm）</summary>
    public double CameraHeightZL_R { get; set; } = 24.7;

    /// <summary>R組：ZR 下降後的檢測高度（mm）</summary>
    public double CameraHeightZR_R { get; set; } = 0.0;

    /// <summary>R組：ZL 安全高度（mm）</summary>
    public double ZLSafeHeight_R { get; set; } = 0.0;

    /// <summary>R組：ZR 安全高度（mm）</summary>
    public double ZRSafeHeight_R { get; set; } = 0.0;

    // =========================================
    //  Z 軸高度設定 — L 組（X13=true，板子在左側）
    //  TODO: 依實際 teaching 結果調整
    // =========================================

    /// <summary>L組：ZL 下降後的檢測高度（mm）</summary>
    public double CameraHeightZL_L { get; set; } = 24.7;

    /// <summary>L組：ZR 下降後的檢測高度（mm）</summary>
    public double CameraHeightZR_L { get; set; } = 0.0;

    /// <summary>L組：ZL 安全高度（mm）</summary>
    public double ZLSafeHeight_L { get; set; } = 0.0;

    /// <summary>L組：ZR 安全高度（mm）</summary>
    public double ZRSafeHeight_L { get; set; } = 0.0;

    // ── 相容舊屬性（讀取時保留，存回新欄位）──
    [JsonIgnore]
    public double CameraHeightZL
    {
        get => CameraHeightZL_R;
        set { CameraHeightZL_R = value; CameraHeightZL_L = value; }
    }
    [JsonIgnore]
    public double CameraHeightZR
    {
        get => CameraHeightZR_R;
        set { CameraHeightZR_R = value; CameraHeightZR_L = value; }
    }
    [JsonIgnore]
    public double ZLSafeHeight
    {
        get => ZLSafeHeight_R;
        set { ZLSafeHeight_R = value; ZLSafeHeight_L = value; }
    }
    [JsonIgnore]
    public double ZRSafeHeight
    {
        get => ZRSafeHeight_R;
        set { ZRSafeHeight_R = value; ZRSafeHeight_L = value; }
    }

    /// <summary>
    /// 依 X12/X13 訊號取得對應的 ZL 檢測高度。
    /// x12=true → R組；x13=true → L組。
    /// </summary>
    public double GetCameraHeightZL(bool x12, bool x13)
        => x12 ? CameraHeightZL_R : CameraHeightZL_L;

    /// <summary>
    /// 依 X12/X13 訊號取得對應的 ZR 檢測高度。
    /// </summary>
    public double GetCameraHeightZR(bool x12, bool x13)
        => x12 ? CameraHeightZR_R : CameraHeightZR_L;

    /// <summary>
    /// 依 X12/X13 訊號取得對應的 ZL 安全高度。
    /// </summary>
    public double GetZLSafeHeight(bool x12, bool x13)
        => x12 ? ZLSafeHeight_R : ZLSafeHeight_L;

    /// <summary>
    /// 依 X12/X13 訊號取得對應的 ZR 安全高度。
    /// </summary>
    public double GetZRSafeHeight(bool x12, bool x13)
        => x12 ? ZRSafeHeight_R : ZRSafeHeight_L;

    /// <summary>
    /// ZL/ZR 判斷「已在安全高度」的容差（mm）。
    /// 實際位置 ? SafeHeight + ZSafeTolerance 視為安全。
    /// </summary>
    public double ZSafeTolerance { get; set; } = 1.0;

    /// <summary>目前套用中的 Recipe 名稱（供UI顯示與下次開窗時還原），空字串代表沒有特定對應的Recipe。</summary>
    public string CurrentRecipeName { get; set; } = "";

    /// <summary>閒置多久（分鐘）自動鎖定畫面，僅Admin可在帳號管理視窗調整。</summary>
    public int IdleLockTimeoutMinutes { get; set; } = 10;

    // =========================================
    //  裁切 ROI 設定（C1~C4 各自獨立）
    // =========================================

    /// <summary>C1 相機 ROI（X13=true, Right 相機）</summary>
    public CameraRoi RoiC1 { get; set; } = new() { CameraId = "C1" };

    /// <summary>C2 相機 ROI（X13=true, Left 相機）</summary>
    public CameraRoi RoiC2 { get; set; } = new() { CameraId = "C2" };

    /// <summary>C3 相機 ROI（X12=true, Right 相機）</summary>
    public CameraRoi RoiC3 { get; set; } = new() { CameraId = "C3" };

    /// <summary>C4 相機 ROI（X12=true, Left 相機）</summary>
    public CameraRoi RoiC4 { get; set; } = new() { CameraId = "C4" };

    /// <summary>依相機名稱取得對應 ROI。</summary>
    public CameraRoi GetRoi(string cameraId) => cameraId switch
    {
        "C1" => RoiC1,
        "C2" => RoiC2,
        "C3" => RoiC3,
        "C4" => RoiC4,
        _    => new CameraRoi()
    };

    // =========================================
    //  BumperFlat 演算法 JSON 設定
    // =========================================

    /// <summary>
    /// 演算法預設 JSON Key（對應 Config 資料夾下的 .json 檔名）。
    /// 例如 "Parameter" → Config/Parameter.json
    /// </summary>
    public string DefaultAlgJsonKey { get; set; } = "Parameter";

    /// <summary>
    /// 依 Slot 位置取得對應的演算法 JSON Key。
    /// 目前所有 Slot 共用同一組參數；
    /// 若未來需要 per-slot 設定，可改回傳 $"{target}_S{slotIndex + 1}"。
    /// </summary>
    public string GetAlgJsonKey(
        SlotInspectionProgress.TargetCollection target,
        int slotIndex)
    {
        return DefaultAlgJsonKey;
    }

    /// <summary>
    /// 根據影像檔名推導演算法 JSON key（比照 TestALG）：
    /// 1) 先取檔名（無副檔名）
    /// 2) 若含至少兩個 '_'，取倒數第二個 '_' 後面的字串（例如 C1_LSlot01）
    /// 3) 只有一個 '_' 則取最後一個 '_' 後面的字串
    /// 4) 回傳空值時 fallback DefaultAlgJsonKey
    /// </summary>
    public string GetAlgJsonKeyFromImagePath(string imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
            return DefaultAlgJsonKey;

        string name = Path.GetFileNameWithoutExtension(imagePath) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
            return DefaultAlgJsonKey;

        // 找倒數第二個 '_'
        int lastIdx = name.LastIndexOf('_');
        if (lastIdx > 0)
        {
            int secondLastIdx = name.LastIndexOf('_', lastIdx - 1);
            int startIdx = secondLastIdx >= 0 ? secondLastIdx + 1 : lastIdx + 1;
            string key = name[startIdx..].Trim();
            if (!string.IsNullOrWhiteSpace(key))
                return key;
        }

        // 檔名沒有底線 → 使用預設 JSON key
        return DefaultAlgJsonKey;
    }

    // =========================================
    //  設定檔存讀（JSON）
    // =========================================

    private static readonly string ConfigFilePath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "Config", "InspectionConfig.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    /// <summary>將目前設定儲存至 Config\InspectionConfig.json。</summary>
    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigFilePath)!);
        File.WriteAllText(ConfigFilePath, JsonSerializer.Serialize(this, JsonOpts));
    }

    /// <summary>從 Config\InspectionConfig.json 載入設定，檔案不存在時回傳預設值。</summary>
    public static InspectionConfig Load()
    {
        if (!File.Exists(ConfigFilePath))
            return new InspectionConfig();
        try
        {
            var json = File.ReadAllText(ConfigFilePath);
            return JsonSerializer.Deserialize<InspectionConfig>(json, JsonOpts) ?? new InspectionConfig();
        }
        catch
        {
            return new InspectionConfig();
        }
    }
}
