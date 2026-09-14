using BarcodeReader.Interfaces;
using BarcodeReader.Services;
using Emgu.CV.XFeatures2D;
using FoupInspecMachine.Helper;
using FoupInspecMachine.Manager;
using Machine.Core;
using Machine.Core.Interfaces;
using MvCodeReaderSDKNet;
using NLog;
using PLC_IO.Interfaces;
using PLC_IO.Services;
using Slot_Inspection.Enum;
using Slot_Inspection.Models;
using System.IO;
using System.IO.Ports;
using System.Runtime.InteropServices;
using static Emgu.CV.WeChatQRCode;


namespace Slot_Inspection.Services;

/// <summary>
/// Machine controller - manages all device init and release.
/// S00 initialization executor.
/// </summary>
public sealed class MachineController : IDisposable
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// 模擬模式：Debug-Sim 組態預設 true（全模擬），其他組態預設 false（實機）。
    /// 可在執行時透過 UI 切換。
    /// </summary>
#if DEBUG_SIM
    public static bool SimulationMode { get; set; } = true;
#else
    public static bool SimulationMode { get; set; } = false;
#endif

    // 實際軸配置（對應 DeltaAxis_RS485 硬體）:
    //   AxisY  = COM12, SlaveId=1  → 載台移動軸
    //   AxisX  = COM13, SlaveId=1  → 讀碼軸
    //   AxisZL = COM14, SlaveId=1  → 左相機高度軸
    //   AxisZR = COM15, SlaveId=1  → 右相機高度軸
    private readonly string[] _axisNames = ["AxisY", "AxisX", "AxisZL", "AxisZR"];

    // Y 軸行程極限（mm）
    private const double YAxisMaxMm = 496.0;

    // 相機 UID：必須與 GrabModule.txt 裡的 "UID" 欄位完全一致
    private const string CameraUidRight = "Right";
    private const string CameraUidLeft = "Left";
    private readonly string[] _cameraNames = [CameraUidRight, CameraUidLeft];

    private readonly string _lightComPort = "COM16"; // TODO: 確認光源實際 COM port
    private readonly string _barcodeIp = "192.168.1.10";
    private readonly int _barcodePort = 8000;

    // -- PLC (三菱 FX) 串口設定 --
    private readonly string _plcComPort = "COM11"; // TODO: 確認 PLC 實際 COM port
    private readonly int _plcBaudRate = 115200;

    private readonly TimeSpan _axisHomeTimeout = TimeSpan.FromSeconds(60);
    private readonly TimeSpan _deviceConnectTimeout = TimeSpan.FromSeconds(10);

    public Dictionary<string, IAxis> Axes => cMachineManager.Axises;
    public Dictionary<string, ICamera> Cameras => cMachineManager.Cameras;

    private HighBright_Controller? _lightHB;           // HighBright 光源（DryRun / 實際取像用）
    private CameraManager? _cameraManager;

    // DryRun 直接操作的相機物件（對應 GrabModule.txt UID: "Right" / "Left"）
    private ICamera? _cameraRight;
    private ICamera? _cameraLeft;

    private ICodeReaderDevice? _barcodeDevice;    // 海康讀碼器 SDK 控制物件
    private IBarcodeResultParser? _barcodeParser;  // 讀碼結果解析器
    private IPlcCommunicator? _plc;               // 三菱 FX PLC 通訊
    private bool _disposed;

    // -- DryRun 暫停/繼續機制（volatile bool + polling，避免 TCS 累積導致效能退化）--
    private volatile bool _dryRunPaused;

    /// <summary>目前是否處於 DryRun 暫停狀態</summary>
    public bool IsDryRunPaused => _dryRunPaused;

    /// <summary>
    /// 暫停空跑流程並立即停止所有軸（X7 光閘觸發時呼叫）。
    /// 電平觸發：只要 X7=0 就保持暫停，重複呼叫無副作用。
    /// </summary>
    public void PauseDryRun()
    {
        if (!_dryRunPaused)
        {
            _dryRunPaused = true;
            // 立即停止所有動作軸
            foreach (var axisName in new[] { "AxisY", "AxisZL", "AxisZR" })
                if (Axes.TryGetValue(axisName, out var ax))
                    try { ax.MotStop(isImmediate: true); } catch { }
            _logger.Info("[DryRun] Paused + axes stopped (X7=0)");
        }
    }

    /// <summary>繼續空跑流程（X10=1 時呼叫，僅在暫停狀態下有效）</summary>
    public void ResumeDryRun()
    {
        if (_dryRunPaused)
        {
            _dryRunPaused = false;
            _logger.Info("[DryRun] Resumed (X10=1)");
        }
    }

    /// <summary>
    /// 立即停止所有軸（供 STOP 按鈕使用）。
    /// 取消 CancellationToken 只能阻止「軟體之後再送出新的移動指令」，
    /// 無法中斷已送出的 MotMoveAbs（TriggerPr1 是 fire-and-forget，驅動器會自己跑完該段行程），
    /// 所以 STOP 必須直接對軸體下 MotStop(isImmediate:true) 才能讓軸真正停在原地。
    /// </summary>
    public void StopAllAxesImmediate(string reason)
    {
        foreach (var axisName in _axisNames)
            if (Axes.TryGetValue(axisName, out var ax))
                try { ax.MotStop(isImmediate: true); }
                catch (Exception ex) { _logger.Warn(ex, $"[StopAllAxesImmediate] {axisName} MotStop 失敗"); }
        _logger.Info($"[StopAllAxesImmediate] 所有軸已下達立即停止指令，原因：{reason}");
    }

    /// <summary>
    /// 若目前處於暫停狀態則以 polling 等待，直到 ResumeDryRun() 被呼叫或取消。
    /// 使用簡單的 volatile bool + Task.Delay polling，
    /// 避免每次 pause/resume 建立 TaskCompletionSource 導致效能退化。
    /// </summary>
    private async Task CheckPauseAsync(IProgress<string>? progress, CancellationToken ct)
    {
        if (!_dryRunPaused) return;
        progress?.Report("[暫停中] 光閘觸發，等待 X10=1 繼續空跑...");
        while (_dryRunPaused)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(100, ct);
        }
    }

    /// <summary>
    /// Y 軸移動前的 ZL/ZR 安全高度保護。
    /// 若 ZL 或 ZR 超過安全高度容差，彈出確認視窗詢問使用者是否先升軸；
    /// 使用者按「是」→ 升至安全高度並等待到位；按「否」→ 拋出 OperationCanceledException。
    /// </summary>
    /// <summary>
    /// 在 Y 軸移動前確認 ZL/ZR 已達安全高度。
    /// 若不安全則記錄詳細 LOG 並詢問使用者是否自動升軸；拒絕則拋出 OperationCanceledException。
    /// </summary>
    /// <param name="caller">呼叫端識別字串，用於 LOG 追蹤（例如 "OnRunInspection-STEP0"）。</param>
    public async Task EnsureZSafeBeforeYMoveAsync(
        double safeZL, double safeZR,
        IProgress<string>? progress, CancellationToken ct,
        string caller = "Unknown")
    {
        double currentZL = Axes["AxisZL"].GetRealPosition();
        double currentZR = Axes["AxisZR"].GetRealPosition();
        double tol = _config.ZSafeTolerance;

        bool zlUnsafe = currentZL > safeZL + tol;
        bool zrUnsafe = currentZR > safeZR + tol;

        // ── LOG：Y 移動前 Z 軸狀態確認 ──
        if (!zlUnsafe && !zrUnsafe)
        {
            _logger.Info(
                $"▶ Y 軸移動前確認：ZL 與 ZR 均在安全高度，可以移動" +
                $"（ZL 實際={currentZL:F2} mm，安全上限={safeZL:F2} mm｜" +
                $"ZR 實際={currentZR:F2} mm，安全上限={safeZR:F2} mm｜來源：{caller}）");
            return;
        }

        // ── LOG：Z 軸超出安全高度，需要先升軸 ──
        string unsafeWho =
            (zlUnsafe && zrUnsafe) ? "ZL 與 ZR 皆超出安全高度" :
            zlUnsafe               ? "ZL 超出安全高度" :
                                     "ZR 超出安全高度";

        string unsafeDetail =
            (zlUnsafe ? $"ZL 實際={currentZL:F2} mm（安全上限={safeZL:F2} mm，超出={(currentZL - safeZL):F2} mm）" : "") +
            (zlUnsafe && zrUnsafe ? "，" : "") +
            (zrUnsafe ? $"ZR 實際={currentZR:F2} mm（安全上限={safeZR:F2} mm，超出={(currentZR - safeZR):F2} mm）" : "");

        _logger.Warn($"⚠ Y 軸移動前安全確認：{unsafeWho}，系統攔截移動！" +
                     $"（{unsafeDetail}｜來源：{caller}）");

        string messageText =
            $"ZL/ZR 尚未回到安全高度，直接移動 Y 軸可能造成碰撞。\n\n" +
            $"ZL 目前 {currentZL:F2} mm（安全上限 {safeZL:F2} mm）\n" +
            $"ZR 目前 {currentZR:F2} mm（安全上限 {safeZR:F2} mm）\n\n" +
            $"是否將 ZL/ZR 升至安全高度後繼續？";

        bool? userConfirm = null;
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            var result = System.Windows.MessageBox.Show(
                messageText,
                "⚠ ZL/ZR 安全高度警告",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning,
                System.Windows.MessageBoxResult.No);
            userConfirm = result == System.Windows.MessageBoxResult.Yes;
        });

        if (userConfirm != true)
        {
            _logger.Warn($"⚠ 使用者選擇不升軸，Y 軸移動取消（來源：{caller}）");
            throw new OperationCanceledException("使用者取消：ZL/ZR 未達安全高度，Y 軸移動中止。");
        }

        _logger.Info($"▶ 使用者確認：開始將 ZL/ZR 升至安全高度（ZL 目標={safeZL:F2} mm，ZR 目標={safeZR:F2} mm）");
        progress?.Report($"正在將 ZL→{safeZL:F2} mm、ZR→{safeZR:F2} mm 升至安全高度...");
        // 對話框期間 OnPlcTick 可能已呼叫 _cts.Cancel()，發出移動指令前先確認仍有效
        ct.ThrowIfCancellationRequested();
        Axes["AxisZL"].MotMoveAbs(safeZL);
        Axes["AxisZR"].MotMoveAbs(safeZR);
        // 等 150ms 讓驅動器清除上一次的 InPosition 旗標，避免 Wait() 提早回傳 true
        await Task.Delay(150, ct);
        bool raised = await WaitUntilAsync(
            () => Axes["AxisZL"].Wait() && Axes["AxisZR"].Wait(),
            _config.MoveTimeout, ct);

        if (!raised)
        {
            double postZL = Axes["AxisZL"].GetRealPosition();
            double postZR = Axes["AxisZR"].GetRealPosition();
            string timeoutMsg =
                $"ZL/ZR 升至安全高度逾時，Y 軸移動中止以避免碰撞" +
                $"（ZL 實際={postZL:F2} mm，目標={safeZL:F2} mm｜" +
                $"ZR 實際={postZR:F2} mm，目標={safeZR:F2} mm｜來源：{caller}）";
            _logger.Error($"✘ {timeoutMsg}");
            progress?.Report($"✘ {timeoutMsg}");
            // 未確認 Z 軸到達安全高度前，絕不允許呼叫端繼續移動 Y 軸
            throw new TimeoutException(timeoutMsg);
        }

        double okZL = Axes["AxisZL"].GetRealPosition();
        double okZR = Axes["AxisZR"].GetRealPosition();
        _logger.Info($"✔ ZL/ZR 已升至安全高度（ZL={okZL:F2} mm，ZR={okZR:F2} mm｜來源：{caller}）");
        progress?.Report($"✔ ZL/ZR 已恢復安全高度（ZL={okZL:F2} mm，ZR={okZR:F2} mm）");
    }

    /// <summary>
    /// Y 軸移動的「最後一道」硬檢查：ZL 或 ZR 只要沒回到安全高度（往下降超過容差）就回傳 (false, 原因)。
    /// 由 <see cref="ZSafeGuardedYAxis"/> 在每次 MotMoveAbs / MotMoveRel 送出前呼叫；不通過就丟例外、指令不送出。
    /// 跟 <see cref="EnsureZSafeBeforeYMoveAsync"/> 用同一套判斷（實際位置 &gt; 安全高度 + 容差 = 不安全），
    /// 安全高度依當下 PLC 偵測到的側別（X12/X13）取 R 組或 L 組；讀不到側別時用兩組裡較嚴格的。
    /// 模擬模式一律放行（cAxis_Simulation 的位置是固定假值，套這道檢查會誤擋）。
    /// </summary>
    private (bool ok, string reason) CheckZSafeForYMoveHardGate()
    {
        if (SimulationMode) return (true, string.Empty);
        try
        {
            double tol = _config.ZSafeTolerance;

            bool? x12 = GetPlcXOctal(CarrierLeftSensorPlcAddr);
            bool? x13 = GetPlcXOctal(CarrierRightSensorPlcAddr);
            double safeZL, safeZR;
            if (x12.HasValue && x13.HasValue)
            {
                safeZL = _config.GetZLSafeHeight(x12.Value, x13.Value);
                safeZR = _config.GetZRSafeHeight(x12.Value, x13.Value);
            }
            else
            {
                // 讀不到側別 → 用兩組裡「較嚴格（較低）」的安全高度當門檻，不放行有疑慮的移動
                safeZL = Math.Min(_config.ZLSafeHeight_R, _config.ZLSafeHeight_L);
                safeZR = Math.Min(_config.ZRSafeHeight_R, _config.ZRSafeHeight_L);
            }

            double zl = Axes["AxisZL"].GetRealPosition();
            double zr = Axes["AxisZR"].GetRealPosition();
            if (zl > safeZL + tol)
                return (false, $"ZL 目前 {zl:F2} mm 未回到安全高度（安全上限 {safeZL:F2} + 容差 {tol:F1} mm）");
            if (zr > safeZR + tol)
                return (false, $"ZR 目前 {zr:F2} mm 未回到安全高度（安全上限 {safeZR:F2} + 容差 {tol:F1} mm）");
            return (true, string.Empty);
        }
        catch (Exception ex)
        {
            // 讀不到 ZL/ZR 位置：保守起見「擋下」，不放行狀態不明的 Y 移動
            return (false, $"無法確認 ZL/ZR 安全高度（{ex.Message}），保守擋下 Y 軸移動");
        }
    }

    /// <summary>把可能被 <see cref="ZSafeGuardedYAxis"/> 包住的軸拆回真正的內層軸（給只吃具體型別的地方用，例如 <c>is cAxis_RS485</c>）。</summary>
    private static IAxis Unwrap(IAxis axis) => axis is ZSafeGuardedYAxis g ? g.Inner : axis;

    private readonly InspectionConfig _config = InspectionConfig.Load();
    public InspectionConfig Config => _config;
    private readonly BumperAlgService _bumperAlg = new();

    // -- Sim image loader (lazy init) --
    private SimImageLoader? _simImageLoader;
    private SimImageLoader GetSimImageLoader()
        => _simImageLoader ??= new SimImageLoader(_config.SimImageFolderPath);

    // -- Two cameras: Left & Right side of bumper --
    private static readonly Machine.Core.NamedKey _cameraKeyLeft = NamedKeyCamera.AreaCameraTop;
    private static readonly Machine.Core.NamedKey _cameraKeyRight = NamedKeyCamera.AreaCameraSide;

    // =========================================
    //  S01: Carrier detection + Barcode scan
    // =========================================

    /// <summary>
    /// 讀取 PLC X 輸入點狀態（直接用 _xData 陣列索引）。
    /// 建議改用 <see cref="GetPlcXOctal"/> 以 PLC 面板位址讀取。
    /// </summary>
    public bool GetPlcX(int index)
    {
        // 先取本地參考再用：InitPlc() 重新初始化時會有一小段 _plc == null 的空窗
        // （舊連線已 Dispose、新連線還沒建好），其他執行緒此時剛好進來不能 NRE / ObjectDisposedException。
        var plc = _plc;
        if (plc == null || !plc.IsConnected) return false;
        try { return plc.GetX(index); }
        catch (ObjectDisposedException) { return false; }
    }

    /// <summary>
    /// 以 PLC 面板標示的八進制位址讀取 X 輸入點。
    /// 三菱 FX X 點位為八進制，FxPlcCommunicator._xData 為十進制索引：
    ///   X0~X7  → index 0~7（相同）
    ///   X10    → index 8
    ///   X12    → index 10
    ///   X13    → index 11
    /// 直接傳入面板上看到的數字即可，例如 X10 傳 10，X7 傳 7。
    /// 回傳 null 表示 PLC 未連線（無法判斷狀態），呼叫端應視為「不確定」而非「觸發」。
    /// </summary>
    public bool? GetPlcXOctal(int plcOctalAddr)
    {
        // IsConnected 只代表底層連線（COM Port）還開著；IsXDataFresh 才是「讀取真的還在正常更新」。
        // 兩者都要成立，否則 GetX() 回傳的可能只是卡住很久的舊快取值——這個 session 已經在
        // PLC 寫入佇列餓死讀取、HighBright 光源控制器連線未斷但停止正確回應這兩個地方各踩過一次，
        // 對安全訊號（尤其是 X7 光閘）不能只信任「連線還開著」。
        var plc = _plc; // 見 GetPlcX 的說明：InitPlc 重連空窗期要能安全處理
        if (plc == null || !plc.IsConnected || !plc.IsXDataFresh) return null;
        try { return plc.GetX(OctalAddrToIndex(plcOctalAddr)); }
        catch (ObjectDisposedException) { return null; }
    }

    /// <summary>
    /// 以 PLC 八進位位址設定 Y 輸出點狀態。
    /// </summary>
    public void SetPlcYOctal(int plcOctalAddr, bool value)
    {
        var plc = _plc; // 見 GetPlcX 的說明：InitPlc 重連空窗期要能安全處理
        if (plc == null || !plc.IsConnected) return;
        try { plc.SetY(OctalAddrToIndex(plcOctalAddr), value); }
        catch (ObjectDisposedException) { }
    }

    /// <summary>
    /// SLC Reset：對 Y0 送出 ON→(100ms)→OFF pulse。
    /// 若需更改目標 Y 點，修改 SlcResetYAddr 常數即可。
    /// </summary>
    private const int SlcResetYAddr = 0; // PLC Y0（八進位位址）
    public async Task SlcResetAsync()
    {
        if (SimulationMode)
        {
            _logger.Info("[SLC] (Sim) SLC Reset pulse");
            await Task.Delay(200);
            return;
        }
        SetPlcYOctal(SlcResetYAddr, true);
        await Task.Delay(100);
        SetPlcYOctal(SlcResetYAddr, false);
        _logger.Info("[SLC] SLC Reset pulse sent (Y0 ON→OFF)");
    }

    /// <summary>
    /// 異常燈號的單一事實來源。所有觸發/解除異常都應透過 Alarms.Raise(code, message) /
    /// Alarms.Clear(code)，不要再直接呼叫 SetRedAlarm。實際寫入 PLC 由 SyncRedAlarmOutput()
    /// 統一負責，並且要用每個 tick 重新斷言的方式呼叫，理由見 <see cref="AlarmManager"/> 上的說明。
    /// </summary>
    public AlarmManager Alarms { get; } = new();

    /// <summary>
    /// 紅燈警示（PLC Y1，八進位位址）：底層原始寫入，只把 Y01 設成呼叫端指定的狀態，
    /// 本身不記錄「為什麼」。外部呼叫請一律改用 Alarms.Raise/Clear，讓 AlarmManager 集中管理
    /// 觸發原因；紅燈實際 ON/OFF 的判斷由 <see cref="SyncRedAlarmOutput"/> 依 Alarms.IsActive 決定。
    /// 若需更改目標 Y 點，修改 RedAlarmYAddr 常數即可。
    /// </summary>
    private const int RedAlarmYAddr = 1; // PLC Y01（八進位位址）
    private bool? _lastSyncedRedAlarmState; // 只在狀態真的改變時寫 log，避免每個 tick 洗版
    public void SetRedAlarm(bool on)
    {
        SetPlcYOctal(RedAlarmYAddr, on);
        _logger.Info($"[Alarm] Y01 紅燈 {(on ? "ON" : "OFF")}");
    }

    /// <summary>
    /// 依 Alarms.IsActive 目前的邏輯狀態，重新把 Y01 斷言為對應的 ON/OFF。
    ///
    /// 必須由呼叫端在固定週期（例如 MainViewModel 既有的 500ms PLC tick）持續呼叫，
    /// 而不是只在異常觸發/解除的當下呼叫一次：Y01 是實體 PLC 線圈，若某次寫入剛好遇到
    /// PLC 未連線（SetPlcYOctal 內部會直接放棄該次寫入），這次「熄燈」或「亮燈」指令就
    /// 會悄悄遺失；靠 tick 持續重新斷言，PLC 重新連線後最多一個 tick（500ms）內就會自動
    /// 補回正確狀態，不需要使用者手動按任何按鈕。
    /// </summary>
    public void SyncRedAlarmOutput()
    {
        bool desired = Alarms.IsActive;
        SetPlcYOctal(RedAlarmYAddr, desired);
        if (_lastSyncedRedAlarmState != desired)
        {
            _lastSyncedRedAlarmState = desired;
            _logger.Info($"[Alarm] Y01 紅燈 {(desired ? "ON" : "OFF")}（同步自 Alarms.IsActive）");
        }
    }

    /// <summary>
    /// 將 PLC 八進制位址（面板標示數字）轉為 _xData 十進制索引。
    /// 例：10（X10） → 8、12（X12） → 10、13（X13） → 11
    /// </summary>
    private static int OctalAddrToIndex(int plcOctalAddr)
    {
        int result = 0, place = 1;
        while (plcOctalAddr > 0)
        {
            result += (plcOctalAddr % 10) * place;
            place *= 8;
            plcOctalAddr /= 10;
        }
        return result;
    }

    /// <summary>載台在席感測器 PLC X 點位（八進制位址）</summary>
    private const int CarrierLeftSensorPlcAddr = 12;   // X12（八進制）→ OctalAddrToIndex = 10
    private const int CarrierRightSensorPlcAddr = 13;   // X13（八進制）→ OctalAddrToIndex = 11

    /// <summary>
    /// 讀取 X12/X13 判斷載台側別（R/L），連續 <paramref name="confirmCount"/> 次讀值一致才確認，
    /// 避免單次 PLC 訊號雜訊/接觸不良瞬間誤判（跟 X3/X11 板子在席感測器的 debounce 精神一致，
    /// 只是這裡是一次性判斷用的輪詢版本，不是持續監控）。
    ///
    /// 注意：跟 UI 上顯示的 R/L（<c>x12 ? "R" : "L"</c>）同一套慣例——X12 為 true 判定 R 側，
    /// 且 X12 優先於 X13（兩者同時為 true 這種矛盾狀態時，仍依此慣例判定為 R，但會記警告 log）。
    /// </summary>
    /// <param name="confirmCount">需連續讀到幾次相同結果才確認（預設 5 次）</param>
    /// <param name="intervalMs">每次讀取間隔（毫秒，預設 50ms）</param>
    /// <param name="maxAttempts">最多嘗試幾次，超過仍未穩定就用最後一次讀值並記警告（預設 40 次，約 2 秒上限）</param>
    public async Task<(bool X12, bool X13)> DetermineCarrierSideDebouncedAsync(
        CancellationToken ct, int confirmCount = 5, int intervalMs = 50, int maxAttempts = 40)
    {
        bool lastX12 = false, lastX13 = false;
        int consistentCount = 0;
        int attempts = 0;

        while (consistentCount < confirmCount && attempts < maxAttempts)
        {
            ct.ThrowIfCancellationRequested();
            attempts++;

            bool x12 = GetPlcXOctal(CarrierLeftSensorPlcAddr) ?? false;
            bool x13 = GetPlcXOctal(CarrierRightSensorPlcAddr) ?? false;

            consistentCount = (attempts > 1 && x12 == lastX12 && x13 == lastX13) ? consistentCount + 1 : 1;
            lastX12 = x12;
            lastX13 = x13;

            if (consistentCount < confirmCount)
                await Task.Delay(intervalMs, ct);
        }

        if (consistentCount < confirmCount)
            _logger.Warn($"[CarrierSide] X12/X13 訊號在 {maxAttempts} 次讀取內未能連續 {confirmCount} 次穩定一致，" +
                $"改用最後一次讀值：X12={lastX12}, X13={lastX13}");

        if (lastX12 && lastX13)
            _logger.Warn("[CarrierSide] X12 與 X13 同時為 true（矛盾狀態，理論上不該同時觸發），依既有慣例（X12 優先）判定為 R 側");

        return (lastX12, lastX13);
    }

    // ── 即時載台側別監控（供畫面即時顯示 R/L，不用等按 RUN）──
    private bool _liveCarrierPrevX12, _liveCarrierPrevX13;
    private int _liveCarrierStableCount;
    private const int LiveCarrierSideDebounceCount = 3; // 連續 3 次 tick（跟 OnPlcTick 週期一致，約 1.5 秒）讀值一致才更新

    /// <summary>
    /// 目前已 debounce 確認的載台側別即時狀態；PLC 尚未連線、或讀值還沒穩定夠 N 次時為 null
    /// （例如剛開機、PLC 斷線中），呼叫端應保留畫面上原本顯示的值，不要清空。
    /// </summary>
    public (bool X12, bool X13)? LiveCarrierSide { get; private set; }

    /// <summary>
    /// 供 <see cref="MainViewModel"/> 的 PLC tick 每次呼叫一次，累積 X12/X13 debounce 狀態，
    /// 連續穩定 <see cref="LiveCarrierSideDebounceCount"/> 次後更新 <see cref="LiveCarrierSide"/>，
    /// 讓畫面上的 R/L 顯示即時反映目前載台狀態。跟 <see cref="DetermineCarrierSideDebouncedAsync"/>
    /// 是兩條獨立的路——這裡只更新畫面顯示用的即時狀態，RUN 當下實際判斷還是走原本那條阻塞式輪詢，
    /// 避免畫面顯示的即時值萬一還沒穩定，反而拖慢或影響到 RUN 流程本身的判斷時機。
    /// </summary>
    private bool? _liveCarrierLastLoggedX12, _liveCarrierLastLoggedX13;

    public void UpdateLiveCarrierSideTick()
    {
        bool? rawX12 = GetPlcXOctal(CarrierLeftSensorPlcAddr);
        bool? rawX13 = GetPlcXOctal(CarrierRightSensorPlcAddr);

        // 診斷用：原始讀值一有變化（含變成/脫離斷線）就記一行，藉此還原真實時間軸，
        // 排查完「即時顯示」這個功能沒問題後可以拿掉這段。
        if (rawX12 != _liveCarrierLastLoggedX12 || rawX13 != _liveCarrierLastLoggedX13)
        {
            _logger.Debug($"[LiveCarrierSide] 原始讀值變化：X12={(rawX12?.ToString() ?? "null(斷線)")} " +
                $"X13={(rawX13?.ToString() ?? "null(斷線)")}，目前 LiveCarrierSide={LiveCarrierSide}");
            _liveCarrierLastLoggedX12 = rawX12;
            _liveCarrierLastLoggedX13 = rawX13;
        }

        if (rawX12 == null || rawX13 == null)
        {
            // PLC 斷線：凍結目前畫面顯示的狀態，不更新計數器、不清空 LiveCarrierSide
            _liveCarrierStableCount = 0;
            return;
        }

        bool x12 = rawX12.Value, x13 = rawX13.Value;
        _liveCarrierStableCount = (x12 == _liveCarrierPrevX12 && x13 == _liveCarrierPrevX13)
            ? _liveCarrierStableCount + 1 : 1;
        _liveCarrierPrevX12 = x12;
        _liveCarrierPrevX13 = x13;

        if (_liveCarrierStableCount >= LiveCarrierSideDebounceCount
            && LiveCarrierSide != (x12, x13))
        {
            _logger.Debug($"[LiveCarrierSide] 確認更新：X12={x12} X13={x13}（累積穩定次數={_liveCarrierStableCount}）");
            LiveCarrierSide = (x12, x13);
        }
    }

    /// <summary>載台在席偵測結果</summary>
    public enum CarrierPosition { None, Left, Right, Both }

    /// <summary>
    /// S01a：讀取 PLC X12（左）/ X13（右）判斷載台在席位置。
    /// 載台在席即表示料已到位，不需額外等待放料步驟。
    /// FxPlcCommunicator 內部會週期性輪詢 PLC，GetX() 直接讀取最新快取值。
    /// </summary>
    public CarrierPosition DetectCarrierPosition()
    {
        if (SimulationMode)
        {
            _logger.Info("[S01] (Sim) Carrier detected: Left");
            return CarrierPosition.Left;
        }

        if (_plc == null || !_plc.IsConnected)
        {
            _logger.Warn("[S01] PLC not connected");
            return CarrierPosition.None;
        }

        // 直接讀取 PLC X 點位快取（由 FxPlcCommunicator 背景輪詢更新）
        // 使用 OctalAddrToIndex 將八進制位址轉為正確的 _xData 索引
        bool left = GetPlcXOctal(CarrierLeftSensorPlcAddr) ?? false;   // X12（八進制）→ index 10
        bool right = GetPlcXOctal(CarrierRightSensorPlcAddr) ?? false;  // X13（八進制）→ index 11

        var position = (left, right) switch
        {
            (true, true) => CarrierPosition.Both,
            (true, false) => CarrierPosition.Left,
            (false, true) => CarrierPosition.Right,
            (false, false) => CarrierPosition.None,
        };

        _logger.Info($"[S01] PLC sensor: X12(Left)={left}, X13(Right)={right} → {position}");
        return position;
    }

    /// <summary>
    /// S01b：依載台位置移動軸到讀碼位置 → 軟體觸發讀碼器 → 回傳條碼字串。
    /// 失敗回傳 null。
    /// 流程：
    ///   1. 自動初始化讀碼器（若尚未連線）
    ///   2. X 軸移動至讀碼位置
    ///   3. 軟體觸發讀碼 → 解析條碼 → 回傳字串
    /// </summary>
    public async Task<string?> MoveAndReadBarcodeAsync(
        CarrierPosition position,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        if (SimulationMode)
        {
            progress?.Report("[Sim] Moving to barcode position...");
            await Task.Delay(500, ct);
            progress?.Report("[Sim] Reading barcode...");
            await Task.Delay(300, ct);
            string simCode = position == CarrierPosition.Right
                ? "SIM-RIGHT-001"
                : "SIM-LEFT-001";
            _logger.Info($"[S01] (Sim) Barcode read: {simCode}");
            return simCode;
        }

        // STEP 0: 若讀碼器尚未初始化，自動初始化
        if (_barcodeDevice == null || !_barcodeDevice.IsConnected)
        {
            progress?.Report("[S01] 初始化讀碼器...");
            var initResult = InitBarcodeReader();
            if (!initResult.Success)
            {
                _logger.Warn($"[S01] 讀碼器初始化失敗: {initResult.Message}");
                progress?.Report($"[S01] ? 讀碼器無法連線: {initResult.Message}");
                return null;
            }
            _logger.Info("[S01] 讀碼器初始化 ?");
            progress?.Report("[S01] 讀碼器就緒 ?");
        }

        // STEP 1: X 軸移動至讀碼位置
        double targetX = _config.GetBarcodePositionX(position);
        progress?.Report($"X 軸移動至讀碼位置（{position}）→ X={targetX:F2} mm...");
        _logger.Info($"[S01] Moving AxisX to barcode pos: {position} → X={targetX}");

        Axes["AxisX"].MotMoveAbs(targetX);

        // 等 150ms 讓驅動器清除上一次的 InPosition 旗標，避免 Wait() 提早回傳 true
        await Task.Delay(150, ct);

        bool arrived = await WaitUntilAsync(
            () => Axes["AxisX"].Wait(),
            _config.MoveTimeout, ct);

        if (!arrived)
        {
            _logger.Warn("[S01] Move to barcode position timeout");
            progress?.Report("移動至讀碼位置逾時");
            return null;
        }
        progress?.Report($"[S01] X 軸到位 ?");

        // STEP 2: 軟體觸發讀碼器取像 + 解析條碼
        progress?.Report("讀碼中...");
        string? code = await ReadBarcodeAsync(ct);

        if (string.IsNullOrEmpty(code))
        {
            // 回傳 null 讓呼叫端走「讀碼失敗 → 退軸 → 重新放置」流程，
            // 不可回傳假條碼，否則檢測結果 / CSV / MES 會掛在錯誤的條碼底下
            _logger.Warn("[S01] 讀碼失敗（NoRead 或 timeout）");
            progress?.Report("[S01] ? 讀碼失敗");
            return null;
        }

        _logger.Info($"[S01] 讀碼成功: {code}");
        progress?.Report($"[S01] 條碼：{code} ?");
        return code;
    }

    /// <summary>
    /// 確保讀碼器已連線後，發送 Software Trigger 讀取條碼並回傳條碼字串。
    /// Simulation 模式下回傳模擬條碼。
    /// </summary>
    public async Task<string?> TriggerReadBarcodeAsync(
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        if (SimulationMode)
        {
            progress?.Report("[Sim] 讀碼中...");
            await Task.Delay(300, ct);
            string simCode = "SIM-BARCODE-001";
            _logger.Info($"[Barcode] (Sim) code: {simCode}");
            return simCode;
        }

        bool justInitialized = false;
        if (_barcodeDevice == null || !_barcodeDevice.IsConnected)
        {
            progress?.Report("初始化讀碼器...");
            var initResult = InitBarcodeReader();
            if (!initResult.Success)
            {
                _logger.Warn($"[Barcode] 讀碼器初始化失敗: {initResult.Message}");
                progress?.Report($"讀碼器無法連線: {initResult.Message}");
                return null;
            }
            justInitialized = true;
        }

        if (justInitialized)
        {
            _logger.Info("[Barcode] 讀碼器剛完成初始化，等待相機穩定 1500ms...");
            await Task.Delay(1500, ct);
        }

        progress?.Report("讀碼中...");
        return await ReadBarcodeAsync(ct);
    }

    /// <summary>
    /// 觸發讀碼器取一幀影像，解析條碼後回傳字串。
    /// 使用 MvCodeReaderSDK 的 Software Trigger 模式。
    /// </summary>
    private async Task<string?> ReadBarcodeAsync(CancellationToken ct = default)
    {
        if (_barcodeDevice == null || _barcodeParser == null)
            return null;

        return await Task.Run(() =>
        {
            // 發送軟體觸發命令，讓讀碼器拍一張照
            _barcodeDevice.SetCommandValue("TriggerSoftware");

            // 配置接收緩衝區
            nint pData = 0;
            nint pFrameInfo = Marshal.AllocHGlobal(
                Marshal.SizeOf<MvCodeReader.MV_CODEREADER_IMAGE_OUT_INFO_EX2>());
            try
            {
                // 等待讀碼器回傳影像（最長 5 秒）
                int ret = _barcodeDevice.GetOneFrameTimeout(ref pData, pFrameInfo, 5000);
                if (ret != 0)
                {
                    _logger.Warn($"Barcode read timeout: 0x{ret:X}");
                    return null;
                }

                // 從影像中解析條碼
                var results = _barcodeParser.Parse(pFrameInfo);
                string? code = results.Count > 0 && results[0].Code != "NoRead"
                    ? results[0].Code
                    : null;

                _logger.Info($"Barcode read: {code ?? "NoRead"}");
                return code;
            }
            finally
            {
                Marshal.FreeHGlobal(pFrameInfo);
            }
        }, ct);
    }

    // =========================================
    //  S00
    // =========================================

    public async Task<MachineInitResult> InitializeAllAsync(
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var result = new MachineInitResult();
        _logger.Info($"===== S00 Init Start (Sim: {SimulationMode}) =====");

        if (SimulationMode)
        {
            string[] steps =
            [
                "Config",
                "3-Axis Servo", "3-Axis Home",
                "PLC (FX)"
            ];
            foreach (var step in steps)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report($"[Sim] {step}...");
                await Task.Delay(300, ct);
                result.Add(DeviceInitResult.Ok($"{step} (Sim)"));
                _logger.Info($"{step} (Sim): OK");
            }
            _logger.Info("===== S00 Sim Init Done =====");
            return result;
        }

        progress?.Report("Loading config...");
        result.Add(InitMachineCore());
        if (!result.AllPassed) return result;

        progress?.Report("Init light...");
        result.Add(InitLightHB());
        if (!result.AllPassed) return result;

        progress?.Report("Enabling servo...");
        result.Add(await InitAxesAsync(ct));
        if (!result.AllPassed) return result;

        progress?.Report("Homing...");
        result.Add(await HomeAllAxesAsync(progress, ct));
        if (!result.AllPassed) return result;

        // TODO: 相機功能暫時停用
        // progress?.Report("Init cameras...");
        // result.Add(await InitCamerasAsync(ct));
        // if (!result.AllPassed) return result;

        // TODO: 海康讀碼器 SDK 原生 DLL
        // progress?.Report("Connecting barcode reader...");
        // result.Add(InitBarcodeReader());

        progress?.Report("Connecting PLC...");
        result.Add(InitPlc());

        _logger.Info("===== S00 Init Done =====");
        _logger.Info(result.GetSummary());
        return result;
    }

    /// <summary>
    /// 裝置初始化（不含原點賦歸）：Config + Servo 激磁 + PLC 連線。
    /// </summary>
    public async Task<MachineInitResult> InitializeDevicesAsync(
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var result = new MachineInitResult();
        _logger.Info($"===== Devices Init Start (Sim: {SimulationMode}) =====");

        if (SimulationMode)
        {
            foreach (var step in new[] { "Config (Sim)", "4-Axis Servo (Sim)", "Camera (Sim)", "PLC (Sim)" })
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report($"[Sim] {step}...");
                await Task.Delay(300, ct);
                result.Add(DeviceInitResult.Ok(step));
            }
            _logger.Info("===== Devices Sim Init Done =====");
            return result;
        }

        progress?.Report("Loading config...");
        result.Add(InitMachineCore());
        if (!result.AllPassed) return result;

        progress?.Report("Enabling servo...");
        result.Add(await InitAxesAsync(ct));
        if (!result.AllPassed) return result;

        // TODO: 相機功能暫時停用
        progress?.Report("Init cameras...");
        result.Add(await InitCamerasAsync(ct));
        if (!result.AllPassed) return result;

        progress?.Report("Connecting PLC...");
        result.Add(InitPlc());

        _logger.Info("===== Devices Init Done =====");
        _logger.Info(result.GetSummary());
        return result;
    }

    /// <summary>
    /// 原點賦歸（所有軸回原點）。需在裝置初始化完成後呼叫。
    /// </summary>
    public async Task<MachineInitResult> HomeAllAxesPublicAsync(
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var result = new MachineInitResult();
        _logger.Info($"===== Homing Start (Sim: {SimulationMode}) =====");

        if (SimulationMode)
        {
            progress?.Report("[Sim] Homing...");
            await Task.Delay(500, ct);
            result.Add(DeviceInitResult.Ok("4-Axis Home (Sim)"));
            _logger.Info("===== Homing Sim Done =====");
            return result;
        }

        progress?.Report("Homing all axes...");
        result.Add(await HomeAllAxesAsync(progress, ct));

        _logger.Info("===== Homing Done =====");
        _logger.Info(result.GetSummary());
        return result;
    }

    /// <summary>
    /// 軸控專用初始化（空跑測試使用）。
    /// 只初始化 Config + Axis + Home，跳過相機、光源、讀碼器、PLC。
    /// </summary>
    public async Task<MachineInitResult> InitializeAxesOnlyAsync(
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var result = new MachineInitResult();
        _logger.Info($"===== Axes-Only Init Start (Sim: {SimulationMode}) =====");

        if (SimulationMode)
        {
            foreach (var step in new[] { "Config (Sim)", "3-Axis Servo (Sim)", "3-Axis Home (Sim)" })
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report($"[Sim] {step}...");
                await Task.Delay(300, ct);
                result.Add(DeviceInitResult.Ok(step));
            }
            _logger.Info("===== Axes-Only Sim Init Done =====");
            return result;
        }

        progress?.Report("Loading config...");
        result.Add(InitMachineCore());
        if (!result.AllPassed) return result;

        progress?.Report("Enabling servo...");
        result.Add(await InitAxesAsync(ct));
        if (!result.AllPassed) return result;

        progress?.Report("Homing all axes...");
        result.Add(await HomeAllAxesAsync(progress, ct));

        _logger.Info("===== Axes-Only Init Done =====");
        _logger.Info(result.GetSummary());
        return result;
    }

    // =========================================
    //  S01c: Barcode axis return to home
    // =========================================

    /// <summary>
    /// 將條碼讀取相關軸（X/Y/Z）回歸原點，讓機台回到待機狀態。
    /// </summary>
    public async Task MoveBarcodeAxisHomeAsync(
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        if (SimulationMode)
        {
            progress?.Report("[Sim] 條碼軸回歸原點...");
            await Task.Delay(500, ct);
            _logger.Info("[S01c] (Sim) Barcode axis homed");
            return;
        }

        progress?.Report("讀碼軸(X)回歸原點...");
        _logger.Info("[S01c] Moving AxisX to home");

        Axes["AxisX"].Home();

        bool done = await WaitUntilAsync(
            () => Axes["AxisX"].Wait(),
            _axisHomeTimeout, ct);

        if (!done)
            _logger.Warn("[S01c] Barcode axis home timeout");
        else
            _logger.Info("[S01c] Barcode axes homed");
    }

    // =========================================
    //  S03: Inspection loop
    // =========================================

    public async Task RunInspectionAsync(
        string barcode,
        IProgress<SlotInspectionProgress>? progress = null,
        CancellationToken ct = default)
    {
        _logger.Info($"===== S03 Inspection Start | Barcode: {barcode} =====");

        // Row1: AreaA_Row1 + AreaB_Row1 同時處理（25 slots）
        var rows = new (SlotInspectionProgress.TargetCollection AreaA,
                        SlotInspectionProgress.TargetCollection AreaB,
                        int Count)[]
        {
            (SlotInspectionProgress.TargetCollection.AreaA_Row1,
             SlotInspectionProgress.TargetCollection.AreaB_Row1, 13),
            (SlotInspectionProgress.TargetCollection.AreaA_Row2,
             SlotInspectionProgress.TargetCollection.AreaB_Row2, 12),
        };


        // 模擬模式：完全跳過軸/光源硬體初始化，下面 InspectOneSlotAsync 每格自己也會走模擬分支，
        // 這裡只是避免在沒有實機連線時，提早因為「光源初始化失敗」例外把整個模擬流程中斷掉。
        if (!SimulationMode)
        {
            Axes["AxisY"].SetMaxVel(3000);
            Axes["AxisZL"].SetMaxVel(2000);
            Axes["AxisZR"].SetMaxVel(2000);

            // 全程開燈（Slot 迴圈開始前開一次，減少每格重複開關）
            // 使用 HighBright 光源（與 S00 InitLightHB 初始化的控制器一致）
            if (_lightHB == null || !_lightHB.IsOpen)
            {
                var lightInit = InitLightHB();
                if (!lightInit.Success)
                    throw new InvalidOperationException($"光源初始化失敗: {lightInit.Message}");
            }
            bool lightOnL = LightSetWithRetry(_config.LightChannelLeft,  _config.LightIntensityLeft,  "S03", "ON");
            bool lightOnR = LightSetWithRetry(_config.LightChannelRight, _config.LightIntensityRight, "S03", "ON");
            if (!lightOnL || !lightOnR)
                _logger.Warn($"[S03] 光源開啟失敗  CH{_config.LightChannelLeft}={lightOnL} CH{_config.LightChannelRight}={lightOnR}，{DescribeLightFailureReason(_lightHB?.LastUnexpectedResponse)}");
            await Task.Delay(_config.LightStabilizeMs, ct);
            _logger.Info("[S03] Lights ON (all-slots mode)");
        }
        else
        {
            _logger.Info("[S03] (Sim) 跳過軸速度設定與光源初始化");
        }

        try
        {
            foreach (var (areaA, areaB, count) in rows)
            {
                for (int i = 0; i < count; i++)
                {
                    ct.ThrowIfCancellationRequested();

                    // 同時對 AreaA 和 AreaB 進行檢測
                    var taskA = InspectOneSlotAsync(barcode, areaA, i, ct);
                    var taskB = InspectOneSlotAsync(barcode, areaB, i, ct);
                    await Task.WhenAll(taskA, taskB);

                    var (valueA, isNgA, imagePathA, imageA, startTimeA, meanIntensityA, distanceA, boxesA) = taskA.Result;
                    var (valueB, isNgB, imagePathB, imageB, startTimeB, meanIntensityB, distanceB, boxesB) = taskB.Result;

                    // 依 X12/X13 判斷 Bumper Position（不透過 DLL）
                    // X12=true → 右側載台 → 右相機=C3 / 左相機=C4
                    // X13=true → 左側載台 → 右相機=C1 / 左相機=C2
                    // 模擬模式下 PLC 可能未連線，GetPlcXOctal 會回傳 null→預設false，
                    // 固定走 C1/C2 標籤即可（模擬資料本來就不代表實際板側）。
                    bool bpX12 = !SimulationMode && (GetPlcXOctal(CarrierLeftSensorPlcAddr) ?? false);
                    string bumperPosRight = bpX12 ? "C3" : "C1";
                    string bumperPosLeft  = bpX12 ? "C4" : "C2";

                    // valueA / valueB 已是 mm?，LowerSpec/UpperSpec 來自 JSON 設定，processStartTime 計算 DuringTime
                    string layerNum = (i + 1).ToString();
                    InspectionResultWriter.WriteSlotResult(barcode, valueA, isNgA, imagePathA,
                        bumperPosition: bumperPosLeft,  layer: layerNum,
                        lowerSpec: _config.ValueLimitLow, upperSpec: _config.ValueLimitHigh,
                        processStartTime: startTimeA);
                    InspectionResultWriter.WriteSlotResult(barcode, valueB, isNgB, imagePathB,
                        bumperPosition: bumperPosRight, layer: layerNum,
                        lowerSpec: _config.ValueLimitLow, upperSpec: _config.ValueLimitHigh,
                        processStartTime: startTimeB);

                    // 同時回報 AreaA 和 AreaB 的進度（UI 同時顯示兩張圖）
                    progress?.Report(new SlotInspectionProgress
                    {
                        Target = areaA,
                        SlotIndex = i,
                        Value = valueA,
                        IsNg = isNgA,
                        MeanIntensity = meanIntensityA,
                        DistanceToRoiCenter = distanceA,
                        DefectBoundingBoxes = boxesA,
                        StatusText = $"[{areaA}] Slot {i + 1}/{count} - {(isNgA ? "NG" : "OK")}",
                        Image = imageA
                    });

                    progress?.Report(new SlotInspectionProgress
                    {
                        Target = areaB,
                        SlotIndex = i,
                        Value = valueB,
                        IsNg = isNgB,
                        MeanIntensity = meanIntensityB,
                        DistanceToRoiCenter = distanceB,
                        DefectBoundingBoxes = boxesB,
                        StatusText = $"[{areaB}] Slot {i + 1}/{count} - {(isNgB ? "NG" : "OK")}",
                        Image = imageB
                    });

                    _logger.Debug($"[S03] {areaA}[{i}] Value={valueA:F2} IsNg={isNgA}");
                    _logger.Debug($"[S03] {areaB}[{i}] Value={valueB:F2} IsNg={isNgB}");
                }
            }
        }
        finally
        {
            // 全程關燈（正常結束、取消、例外都必須關燈）；模擬模式根本沒開燈，不需要關
            if (!SimulationMode)
            {
                LightSetWithRetry(_config.LightChannelLeft,  0, "S03", "OFF");
                LightSetWithRetry(_config.LightChannelRight, 0, "S03", "OFF");
                _logger.Info("[S03] Lights OFF");
            }
        }

        _logger.Info($"===== S03 Inspection Done | Barcode: {barcode} =====");
    }

    /// <summary>
    /// Single Slot inspection (7 steps):
    ///   Move axis -> Light on -> Capture -> Save -> Light off -> Measure -> NG judge
    /// Simulation: bypass all hardware, generate fake data + test image.
    /// </summary>
    private async Task<(double value, bool isNg, string imagePath, System.Windows.Media.ImageSource? image, DateTime startTime,
        double meanIntensity, double distanceToRoiCenter, IReadOnlyList<System.Drawing.Rectangle> defectBoxes)>
        InspectOneSlotAsync(
            string barcode,
            SlotInspectionProgress.TargetCollection target,
            int slotIndex,
            CancellationToken ct)
    {
        // ==================================
        //  Simulation mode
        // ==================================
        var slotStartTime = DateTime.Now;

        if (SimulationMode)
        {
            await Task.Delay(150, ct);
            string slotLabel = $"{target}_S{slotIndex + 1}";

            var loader = GetSimImageLoader();
            string? simPath = loader.NextPath();
            if (!string.IsNullOrEmpty(simPath))
            {
                string jsonKey = _config.GetAlgJsonKeyFromImagePath(simPath);
                string jsonFullPath = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory, "Config", jsonKey + ".json");
                _logger.Info($"[S03-Sim] {slotLabel} simPath={simPath}, jsonKey={jsonKey}, jsonExists={File.Exists(jsonFullPath)}");

                System.Diagnostics.Debug.WriteLine(
                    $"[S03-Sim] slot={slotLabel}, simPath={simPath}, file={Path.GetFileName(simPath)}");

                var algResult = await _bumperAlg.AnalyzeAsync(simPath, jsonKey, slotLabel, _config.UseNewBumperAlgorithm);

                if (algResult.Success && algResult.Image != null)
                {
                    double simValueOk = algResult.DetectedArea > 0 ? algResult.DetectedArea : 0.0;
                    bool simAlgNg = simValueOk < _config.ValueLimitLow || simValueOk > _config.ValueLimitHigh;
                    _logger.Debug($"[S03-Sim] {slotLabel} ALG OK, json={jsonKey}, value={simValueOk:F4}, isNg={simAlgNg}");
                    return (simValueOk, simAlgNg, simPath, algResult.Image, slotStartTime,
                        algResult.MeanIntensity, algResult.DistanceToRoiCenter, algResult.DefectBoundingBoxes);
                }

                _logger.Warn($"[S03-Sim] {slotLabel} ALG failed, json={jsonKey}, msg={algResult.Message}");
            }

            // fallback: 直接顯示原圖（不再用 SimImageGenerator 假圖）
            if (!string.IsNullOrEmpty(simPath) && File.Exists(simPath))
            {
                var fallbackImage = SimImageLoader.LoadFileAsBitmapSource(simPath);
                if (fallbackImage != null)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[S03-Sim] {slotLabel} fallback: 顯示原圖 {Path.GetFileName(simPath)}");
                    return (1.0, false, simPath, fallbackImage, slotStartTime, 0.0, 0.0, []);
                }
            }

            // 最終 fallback: 無圖可用時才用生成假圖
            var rng = new Random();
            double simL = Math.Round(0.45 + rng.NextDouble() * 0.1, 2);
            double simR = Math.Round(0.45 + rng.NextDouble() * 0.1, 2);
            double simValue = (simL + simR) / 2.0;
            bool simNg = simValue < _config.ValueLimitLow
                      || simValue > _config.ValueLimitHigh;

            System.Windows.Media.ImageSource? simImage = SimImageGenerator.Generate(slotLabel, simValue, simNg);
            return (simValue, simNg, "", simImage, slotStartTime, 0.0, 0.0, []);
        }

        // ==================================
        //  Real mode: 7 steps
        // ==================================

        string slotName = $"{target}_Slot{slotIndex + 1}";

        // STEP 1: Y 軸移動載台到 Slot 位置，ZL/ZR 調整相機高度
        bool s3x12 = GetPlcXOctal(CarrierLeftSensorPlcAddr) ?? false;
        bool s3x13 = GetPlcXOctal(CarrierRightSensorPlcAddr) ?? false;
        var pos    = SlotPositionTable.GetByCarrier(target, slotIndex, isR: s3x12);
        await EnsureZSafeBeforeYMoveAsync(
            _config.GetZLSafeHeight(s3x12, s3x13),
            _config.GetZRSafeHeight(s3x12, s3x13),
            null, ct, "S03-MoveToSlot");

        string s3group  = s3x12 ? "R載台(X12)" : "L載台(X13)";
        _logger.Info($"▶ 開始量測 Slot {slotIndex + 1}（{slotName}，{s3group}，Y 目標={pos.Y:F2} mm）");

        double s3zCamZL = _config.GetCameraHeightZL(s3x12, s3x13);
        double s3zCamZR = _config.GetCameraHeightZR(s3x12, s3x13);
        _logger.Info($"▶ Slot {slotIndex + 1} 移動軸：Y→{pos.Y:F2} mm，ZL→{s3zCamZL:F2} mm，ZR→{s3zCamZR:F2} mm（{s3group}）");
        Axes["AxisY"].MotMoveAbs(pos.Y);
        Axes["AxisZL"].MotMoveAbs(s3zCamZL);
        Axes["AxisZR"].MotMoveAbs(s3zCamZR);

        // 等 150ms 讓驅動器清除上一次的 InPosition 旗標，避免 Wait() 提早回傳 true
        await Task.Delay(150, ct);

        bool arrived = await WaitUntilAsync(
            () => Axes["AxisY"].Wait() && Axes["AxisZL"].Wait() && Axes["AxisZR"].Wait(),
            _config.MoveTimeout, ct);

        if (!arrived)
        {
            double tY = Axes["AxisY"].GetRealPosition();
            double tZL = Axes["AxisZL"].GetRealPosition();
            double tZR = Axes["AxisZR"].GetRealPosition();
            _logger.Warn($"⚠ Slot {slotIndex + 1} 軸移動逾時，請確認設備狀態" +
                         $"（Y 實際={tY:F2} mm，ZL 實際={tZL:F2} mm，ZR 實際={tZR:F2} mm）");
        }
        else
        {
            double rY = Axes["AxisY"].GetRealPosition();
            double rZL = Axes["AxisZL"].GetRealPosition();
            double rZR = Axes["AxisZR"].GetRealPosition();
            _logger.Info($"✔ Slot {slotIndex + 1} 所有軸到位" +
                         $"（Y={rY:F2} mm｜ZL={rZL:F2} mm，目標={s3zCamZL:F2} mm｜ZR={rZR:F2} mm，目標={s3zCamZR:F2} mm）");
        }

        // STEP 1.5: 等待 ZL/ZR 機械振動平息，避免拍照晃動
        if (_config.EnableAxisSettleDelay && _config.AxisSettleDelayMs > 0)
        {
            _logger.Info($"▶ Slot {slotIndex + 1} 等待機械振動穩定（{_config.AxisSettleDelayMs} ms）");
            await Task.Delay(_config.AxisSettleDelayMs, ct);
        }

        // STEP 2: (燈源已在 RunInspectionAsync 全程開啟，此處不重複開燈)

        // STEP 3: Both cameras trigger simultaneously
        //_cameraManager!.CameraStart(_cameraKeyLeft);
        //_cameraManager.CameraStart(_cameraKeyRight);
        //await Task.Delay(_config.CaptureWaitMs, ct);

        // STEP 4: Get both images
        //var imageLeft  = _cameraManager.GetCameraImage(_cameraKeyLeft);
        //var imageRight = _cameraManager.GetCameraImage(_cameraKeyRight);

        // STEP 5: (燈源將在 RunInspectionAsync 結束時統一關閉，此處不重複關燈)

        // Capture failure check
        //if (imageLeft == null || imageRight == null)
        //{
        //    string failSide = imageLeft == null && imageRight == null ? "BOTH"
        //                    : imageLeft == null ? "LEFT" : "RIGHT";
        //    _logger.Warn($"[S03] {slotName} {failSide} capture failed, treat as NG");
        //    return (0, true, "", null);
        //}

        // STEP 6: Save images
        // 命名規則：{yyyy-MM-ddTHH-mm-ss}_{barcodeId}_{C編號}_{LSlot/RSlot}{序號}.bmp
        // 例：2026-04-10T10-34-57_SIM-LEFT-001_C1_LSlot02.bmp
        string imagePath = "";
        string pathL = "", pathR = "";
        //if (_config.SaveImages)
        //{
        //    string ts       = DateTime.Now.ToString("yyyy-MM-ddTHH-mm-ss");
        //    string barcodeId = string.IsNullOrWhiteSpace(barcode) ? "UNKNOWN" : barcode;
        //    bool sx12 = GetPlcXOctal(CarrierLeftSensorPlcAddr)  ?? false;  // X12 → R側
        //    bool sx13 = GetPlcXOctal(CarrierRightSensorPlcAddr) ?? false;  // X13 → L側
        //    string camL   = sx12 ? "C4" : "C2";          // 左相機編號
        //    string camR   = sx12 ? "C3" : "C1";          // 右相機編號
        //    string slotSide = sx12 ? "RSlot" : "LSlot";  // R側 or L側
        //    string slotNum  = (25 - slotIndex).ToString("D2");  // 01 ~ 25
        //    string slotTag  = $"{slotSide}{slotNum}";           // e.g. LSlot02
        //
        //    string dir = Path.Combine(
        //        _config.ImageSavePath,
        //        DateTime.Now.ToString("yyyyMMdd"),
        //        barcodeId);
        //    Directory.CreateDirectory(dir);
        //
        //    pathL = Path.Combine(dir, $"{ts}_{barcodeId}_{camL}_{slotTag}.bmp");
        //    pathR = Path.Combine(dir, $"{ts}_{barcodeId}_{camR}_{slotTag}.bmp");
        //
        //    _cameraManager.SaveCameraImage(_cameraKeyLeft,  pathL, barcode, $"{slotTag}_{camL}");
        //    _cameraManager.SaveCameraImage(_cameraKeyRight, pathR, barcode, $"{slotTag}_{camR}");
        //
        //    imagePath = pathL;
        //}

        // STEP 7: 呼叫 BumperFlat 演算法分析（使用已存檔的 .tif 路徑）
        string algJsonKey = _config.GetAlgJsonKey(target, slotIndex);

        var algResultL = await _bumperAlg.AnalyzeAsync(pathL, algJsonKey, $"{slotName}_L", _config.UseNewBumperAlgorithm);
        var algResultR = await _bumperAlg.AnalyzeAsync(pathR, algJsonKey, $"{slotName}_R", _config.UseNewBumperAlgorithm);

        _logger.Debug($"[S03] {slotName} ALG L={algResultL.Message} R={algResultR.Message}");

        // STEP 8: NG 判斷（依使用者設定的上下限）
        // DetectedArea 在 BumperAlgService.Analyze() 內已乘過 0.0069²，此處直接加總即可
        double measuredValue = algResultL.DetectedArea + algResultR.DetectedArea;
        bool isNg = measuredValue < _config.ValueLimitLow || measuredValue > _config.ValueLimitHigh;

        // 優先顯示左側疊加結果影像
        bool showLeft = algResultL.Image != null;
        System.Windows.Media.ImageSource? displayImage = algResultL.Image ?? algResultR.Image;
        // 缺陷外框要跟著顯示的圖片來源走，否則框會疊在不同相機拍的圖上而錯位
        IReadOnlyList<System.Drawing.Rectangle> defectBoxes =
            showLeft ? algResultL.DefectBoundingBoxes : algResultR.DefectBoundingBoxes;

        // MeanIntensity / DistanceToRoiCenter 為左右相機平均（DetectedArea 是面積故用加總，這兩者是強度/距離量測故取平均）
        double meanIntensity = (algResultL.MeanIntensity + algResultR.MeanIntensity) / 2.0;
        double distanceToRoiCenter = (algResultL.DistanceToRoiCenter + algResultR.DistanceToRoiCenter) / 2.0;

        return (measuredValue, isNg, imagePath, displayImage, slotStartTime, meanIntensity, distanceToRoiCenter, defectBoxes);
    }

    // =========================================
    //  Device init methods
    // =========================================

    private DeviceInitResult InitMachineCore()
    {
        const string name = "Config";
        try
        {
            // 與 CameraLightTest 相同的設定資料夾結構：
            //   <exe>\MachineAssembly\Slot_Inspection\GrabModule.txt  ← 相機定義
            //   <exe>\MachineAssembly\Slot_Inspection\Axises.txt      ← 軸定義
            string baseDir = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "MachineAssembly",
                "Slot_Inspection");
            cMachineManager.Init(baseDir);
            _logger.Info($"{name}: OK (BaseDir={baseDir})");
            return DeviceInitResult.Ok(name);
        }
        catch (Exception ex) { _logger.Error(ex, $"{name}: FAIL"); return DeviceInitResult.Fail(name, $"Load failed: {ex.Message}", ex); }
    }

    /// <summary>
    /// 初始化 HighBright 光源控制器（DryRun / 取像用）。
    /// COM port 與光源 COM port 共用 _lightComPort 設定。
    /// </summary>
    private DeviceInitResult InitLightHB()
    {
        const string name = "HighBright Light";
        try
        {
            // 跟 InitPlc 同理：重複初始化前要先 Dispose 舊的 HighBright_Controller，
            // 否則舊 instance 的 SerialPort 會一直握著光源 COM Port，
            // 新的 Connect() → _port.Open() 會被拒（Access to the path 'COMxx' is denied）。
            if (_lightHB != null)
            {
                try { _lightHB.Dispose(); }
                catch (Exception ex) { _logger.Warn(ex, "[Light] 關閉舊光源連線時發生例外（忽略，繼續建立新連線）"); }
                _lightHB = null;
            }

            _lightHB = new HighBright_Controller(_lightComPort);
            _lightHB.Connect();
            if (!_lightHB.IsOpen)
                return DeviceInitResult.Fail(name, $"Cannot open {_lightComPort}");
            _logger.Info($"{name}: OK ({_lightComPort})");
            return DeviceInitResult.Ok(name);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, $"{name}: FAIL");
            return DeviceInitResult.Fail(name, $"{_lightComPort}: {ex.Message}", ex);
        }
    }

    private async Task<DeviceInitResult> InitAxesAsync(CancellationToken ct)
    {
        const string name = "4-Axis Servo";
        try
        {
            foreach (var axisName in _axisNames)
            {
                ct.ThrowIfCancellationRequested();
                if (!Axes.TryGetValue(axisName, out var axis))
                    return DeviceInitResult.Fail(name, $"Axis not found: {axisName}");

                // RS485 軸需要先呼叫 Connect() 建立 Modbus RTU 連線
                // （Y 軸可能已被 ZSafeGuardedYAxis 包住，先 Unwrap 才拿得到 cAxis_RS485）
                if (Unwrap(axis) is cAxis_RS485 rs485Axis)
                {
                    _logger.Info($"[S00] {axisName} Connecting RS485 ({rs485Axis.PortName}, Baud={rs485Axis.BaudRate}, Slave={rs485Axis.SlaveId})...");
                    rs485Axis.Connect();
                    _logger.Info($"[S00] {axisName} RS485 Connected OK");
                }

                axis.ResetError();
                axis.SetSVON(true);
                bool ready = await WaitUntilAsync(
                    () => axis.GetRDY() && !axis.GetAlarm(),
                    _deviceConnectTimeout, ct);
                if (!ready)
                {
                    if (axis.GetAlarm())
                        Alarms.Raise($"AxisAlarm:{axisName}", $"{axisName} 有警報，初始化失敗");
                    return DeviceInitResult.Fail(name,
                        $"{axisName} not ready (RDY={axis.GetRDY()}, Alarm={axis.GetAlarm()})");
                }
                Alarms.Clear($"AxisAlarm:{axisName}");
                _logger.Debug($"{axisName} ServoOn OK");

                // Y 軸連上、Servo On 後，套上「ZL/ZR 未在安全高度就擋下 Y 移動」的安全外殼（結構性保證）。
                // 之後所有 Axes["AxisY"].MotMoveAbs/MotMoveRel 都會先過 CheckZSafeForYMoveHardGate。
                if (axisName == "AxisY" && Axes[axisName] is not ZSafeGuardedYAxis)
                {
                    Axes[axisName] = new ZSafeGuardedYAxis(Axes[axisName], CheckZSafeForYMoveHardGate);
                    _logger.Info("[S00] AxisY 已套用 Z 安全高度移動保護（ZSafeGuardedYAxis）");
                }
            }
            _logger.Info($"{name}: OK");
            return DeviceInitResult.Ok(name);
        }
        catch (OperationCanceledException) { return DeviceInitResult.Fail(name, "Cancelled"); }
        catch (Exception ex) { _logger.Error(ex, $"{name}: FAIL"); return DeviceInitResult.Fail(name, ex.Message, ex); }
    }

    private async Task<DeviceInitResult> HomeAllAxesAsync(IProgress<string>? progress, CancellationToken ct)
    {
        const string name = "4-Axis Home";
        try
        {

            _plc?.SetY(0, true);

            // ── Step 0：先讓 ZL / ZR 升至安全高度，避免碰撞 ──
            progress?.Report("ZL / ZR 上升至安全高度...");
            if (Axes.TryGetValue("AxisZL", out var zl) && Axes.TryGetValue("AxisZR", out var zr))
            {
                zl.MotMoveAbs(_config.ZLSafeHeight);
                zr.MotMoveAbs(_config.ZRSafeHeight);
                // 等 150ms 讓驅動器清除上一次的 InPosition 旗標，避免 Wait() 提早回傳 true
                await Task.Delay(150, ct);
                bool zlzrUp = await WaitUntilAsync(
                    () => zl.Wait() && zr.Wait(),
                    _config.MoveTimeout, ct);
                if (!zlzrUp)
                    return DeviceInitResult.Fail(name, "ZL/ZR 上升至安全高度逾時");
                _logger.Info("ZL/ZR 已上升至安全高度");
            }

            // ── Step 1：先 home X, ZL, ZR（不含 Y） ──
            progress?.Report("X / ZL / ZR 軸歸原點...");
            string[] nonYAxes = ["AxisX", "AxisZL", "AxisZR"];
            foreach (var axisName in nonYAxes)
            {
                ct.ThrowIfCancellationRequested();
                if (!Axes.TryGetValue(axisName, out var axis))
                    return DeviceInitResult.Fail(name, $"{axisName} 不存在");
                if (axis is cAxis_RS485 rs485)
                    rs485.Controller.SetHomingSpeed(highSpeedRpm: 8000, lowSpeedRpm: 800);
                axis.Home();
                _logger.Debug($"{axisName} Home() issued");
            }

            // ── 等 ZL/ZR/X 完成，逾時才安全讓 Y 動 ──
            // Delta 驅動器的 Home() 是把賦歸路徑整個交給驅動器內部搜尋（P5.007 觸發），
            // 軟體無法控制它下降多少，因此在等待期間即時監控 ZL/ZR 實際位置，
            // 逼近拍照高度（撞機風險區）就立即停軸，避免撞到載台上的板子。
            bool nonYDone = await WaitHomeWithSafetyAsync(nonYAxes, _axisHomeTimeout, ct, BuildZHomeDangerThresholds());
            if (!nonYDone)
                return DeviceInitResult.Fail(name, $"X/ZL/ZR Home timeout 或 Z 軸下降超出安全範圍");

            // ── Step 2：再 home Y ──
            progress?.Report("Y 軸歸原點...");
            if (!Axes.TryGetValue("AxisY", out var axisY))
                return DeviceInitResult.Fail(name, "AxisY 不存在");
            // AxisY 可能已被 ZSafeGuardedYAxis 包住，Unwrap 才拿得到 cAxis_RS485
            if (Unwrap(axisY) is cAxis_RS485 yRs485)
                yRs485.Controller.SetHomingSpeed(highSpeedRpm: 8000, lowSpeedRpm: 800);
            axisY.Home();

            bool yDone = await WaitHomeWithSafetyAsync(["AxisY"], _axisHomeTimeout, ct);
            if (!yDone)
                return DeviceInitResult.Fail(name, "AxisY Home timeout");

            // ── 確認狀態 ──
            foreach (var axisName in _axisNames)
            {
                if (!Axes.TryGetValue(axisName, out var axis)) continue;
                if (!axis.GetOrg())
                    _logger.Warn($"{axisName} ORG not confirmed");
                if (axis.GetAlarm())
                {
                    Alarms.Raise($"AxisAlarm:{axisName}", $"{axisName} 原點賦歸後仍有警報");
                    return DeviceInitResult.Fail(name, $"{axisName} alarm after home");
                }
                Alarms.Clear($"AxisAlarm:{axisName}");
            }

            _logger.Info($"{name}: OK");
            return DeviceInitResult.Ok(name);




        }
        catch (OperationCanceledException)
        {
            foreach (var axisName in _axisNames)
                if (Axes.TryGetValue(axisName, out var ax)) try { ax.MotStop(isImmediate: true); } catch { }
            return DeviceInitResult.Fail(name, "Cancelled, all axes stopped");
        }
        catch (Exception ex) { _logger.Error(ex, $"{name}: FAIL"); return DeviceInitResult.Fail(name, ex.Message, ex); }
    }

    /// <summary>
    /// ZL/ZR 原點賦歸期間允許下降的安全餘裕（mm）。
    /// Delta 驅動器 Home() 內部搜尋路徑會下降多少不受軟體控制，
    /// 只能監控實際位置，逼近拍照高度前先停軸。
    /// </summary>
    private const double HomeDescentSafetyMarginMm = 3.0;

    /// <summary>
    /// 計算 ZL/ZR 原點賦歸期間的下降安全上限：
    /// 取 R組/L組 CameraHeight（拍照高度，即撞機風險區）中較保守（較小）的值，再扣安全餘裕。
    /// 賦歸當下不一定知道是哪一組載台，所以用兩組中較危險的當基準。
    /// </summary>
    private Dictionary<string, double> BuildZHomeDangerThresholds()
    {
        return new Dictionary<string, double>
        {
            ["AxisZL"] = Math.Min(_config.CameraHeightZL_R, _config.CameraHeightZL_L) - HomeDescentSafetyMarginMm,
            ["AxisZR"] = Math.Min(_config.CameraHeightZR_R, _config.CameraHeightZR_L) - HomeDescentSafetyMarginMm,
        };
    }

    /// <summary>
    /// 等待所有軸完成原點賦歸，並同時監控 X7 光閘訊號。
    /// X7=0 → 立即停軸 → 等待 X10=1（上升邊緣）→ 重新發出 Home 指令 → 繼續等待。
    /// 若提供 zDangerThresholds，會同時監控對應軸的實際位置，逼近危險高度立即停軸並回傳 false。
    /// </summary>
    private async Task<bool> WaitHomeWithSafetyAsync(
        string[] nonYAxes, TimeSpan HomeTimeout, CancellationToken ct,
        Dictionary<string, double>? zDangerThresholds = null)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        while (sw.Elapsed < _axisHomeTimeout)
        {
            ct.ThrowIfCancellationRequested();

            // ── Z 軸下降安全監控：逼近拍照高度（撞機風險區）立即停軸 ──
            if (zDangerThresholds != null)
            {
                foreach (var axisName in nonYAxes)
                {
                    if (!zDangerThresholds.TryGetValue(axisName, out var dangerLimit)) continue;
                    if (!Axes.TryGetValue(axisName, out var zAxis)) continue;

                    double pos = zAxis.GetRealPosition();
                    if (pos >= dangerLimit)
                    {
                        foreach (var n in nonYAxes)
                            if (Axes.TryGetValue(n, out var ax)) try { ax.MotStop(isImmediate: true); } catch { }

                        _logger.Error($"[Home] {axisName} 原點賦歸下降超出安全範圍（實際={pos:F2} mm，上限={dangerLimit:F2} mm），已緊急停止");
                        Alarms.Raise($"AxisHomeDanger:{axisName}",
                            $"{axisName} 原點賦歸下降超出安全範圍（實際={pos:F2} mm），已緊急停止，請人工確認後再重新賦歸");
                        return false;
                    }
                }
            }

            // 光閘（X7）監控統一由 MainViewModel.OnPlcTick 處理：觸發時會 _cts.Cancel() +
            // StopAllAxesImmediate（涵蓋全部軸，包含正在歸原點的軸），OnHome() 用的也是同一個
            // _cts，這裡不需要另外的 X7 偵測。

            // 檢查所有軸是否已到位
            if (_axisNames.All(n => Axes.TryGetValue(n, out var ax) && ax.Wait()))
                return true;

            await Task.Delay(100, ct);
        }

        return false; // timeout
    }

    private async Task<DeviceInitResult> InitCamerasAsync(CancellationToken ct = default)
    {
        const string name = "FLIR Camera (DALSA)";
        try
        {
            // 已初始化過就直接回傳成功，避免重複呼叫 Init() 導致 Sapera SDK
            // 將相同 Handle 二次加入 Dictionary 而拋出 duplicate key 例外
            if (_cameraRight != null && _cameraLeft != null)
            {
                _logger.Info($"{name}: already initialized, skip");
                return DeviceInitResult.Ok(name);
            }

            // CameraManager 建構子內部會用 NamedKey 查 Dictionary<string,ICamera>
            // 導致大量 KeyNotFoundException 並在 InitImageBuffer 時 crash (exit code 1)。
            // 改為直接對 ICamera 呼叫 Init()，並用 Task.Run 避免
            // Sapera SDK m_Xfer.Create() 同步阻塞 UI。

            if (!Cameras.TryGetValue(CameraUidRight, out var camR))
                return DeviceInitResult.Fail(name, $"Camera not found: {CameraUidRight}");

            bool rightOk = await Task.Run(() => camR.Init(), ct);
            if (!rightOk)
                return DeviceInitResult.Fail(name, $"{CameraUidRight} Init() 回傳 false");
            _cameraRight = camR;
            _logger.Debug($"[S00] {CameraUidRight} Init OK  W={camR.FrameWidth} H={camR.BufHeight}");

            if (!Cameras.TryGetValue(CameraUidLeft, out var camL))
                return DeviceInitResult.Fail(name, $"Camera not found: {CameraUidLeft}");

            bool leftOk = await Task.Run(() => camL.Init(), ct);
            if (!leftOk)
                return DeviceInitResult.Fail(name, $"{CameraUidLeft} Init() 回傳 false");
            _cameraLeft = camL;
            _logger.Debug($"[S00] {CameraUidLeft} Init OK  W={camL.FrameWidth} H={camL.BufHeight}");

            _logger.Info($"{name}: OK");
            return DeviceInitResult.Ok(name);
        }
        catch (OperationCanceledException)
        {
            return DeviceInitResult.Fail(name, "Cancelled");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, $"{name}: FAIL");
            return DeviceInitResult.Fail(name, ex.Message, ex);
        }
    }

    private DeviceInitResult InitBarcodeReader()
    {
        const string name = "Barcode Reader";
        try
        {
            // 使用 BarcodeReader 專案的 MvDeviceEnumerator 列舉 GigE 裝置
            var enumerator = new MvDeviceEnumerator();
            var devices = enumerator.EnumerateDevices();

            if (devices.Count == 0)
                return DeviceInitResult.Fail(name, "No MvCodeReader device found (請確認讀碼器 GigE 連線)");

            // 優先選擇 UserDefinedName="Barcode" 的裝置；找不到則退回第一台
            int devId = 0;
            bool foundNamed = false;
            for (int i = 0; i < devices.Count; i++)
            {
                if (devices[i].UserDefinedName == "Barcode")
                {
                    devId = i;
                    foundNamed = true;
                    break;
                }
            }
            if (!foundNamed)
                _logger.Warn($"[S01] 未找到 UserDefinedName=\"Barcode\" 的讀碼器，改用第一台");

            _logger.Info($"[S01] 找到 {devices.Count} 台讀碼器，使用: {devices[devId].DisplayName}");

            // 重複初始化（前一次連上後又斷線）前，先銷毀舊的 handle，避免 SDK handle 洩漏。
            // 讀碼器是 GigE 不是 COM Port，不會有「Access denied」，但長時間多次重連仍可能累積未釋放的 handle。
            if (_barcodeDevice != null)
            {
                try { _barcodeDevice.Dispose(); }
                catch (Exception ex) { _logger.Warn(ex, "[S01] 銷毀舊讀碼器 handle 時發生例外（忽略，繼續建立新的）"); }
                _barcodeDevice = null;
                _barcodeParser = null;
            }

            // 建立 BarcodeReader 專案的 SDK 封裝物件
            _barcodeDevice = new MvCodeReaderDevice();
            _barcodeParser = new MvBarcodeResultParser();

            // 建立 Handle
            int ret = _barcodeDevice.CreateHandle(devices[devId].RawDeviceInfo!);
            if (ret != 0)
                return DeviceInitResult.Fail(name, $"CreateHandle failed: 0x{ret:X}");

            // 開啟裝置
            ret = _barcodeDevice.OpenDevice();
            if (ret != 0)
            {
                _barcodeDevice.DestroyHandle();
                return DeviceInitResult.Fail(name, $"OpenDevice failed: 0x{ret:X}");
            }

            // 設定軟體觸發模式（由程式決定何時讀碼）
            _barcodeDevice.SetEnumValue("TriggerMode",
                (uint)MvCodeReader.MV_CODEREADER_TRIGGER_MODE.MV_CODEREADER_TRIGGER_MODE_ON);
            _barcodeDevice.SetEnumValue("TriggerSource",
                (uint)MvCodeReader.MV_CODEREADER_TRIGGER_SOURCE.MV_CODEREADER_TRIGGER_SOURCE_SOFTWARE);

            // 進入等待觸發狀態
            ret = _barcodeDevice.StartGrabbing();
            if (ret != 0)
                return DeviceInitResult.Fail(name, $"StartGrabbing failed: 0x{ret:X}");

            _logger.Info($"{name}: OK ({devices[devId].DisplayName})");
            return DeviceInitResult.Ok(name);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, $"{name}: FAIL");
            return DeviceInitResult.Fail(name, ex.Message, ex);
        }
    }

    /// <summary>
    /// 初始化 PLC 通訊（三菱 FX 系列）。
    /// 使用 PLC_IO 專案的 SerialBytesCommunicator（串口 Transport）
    /// 搭配 FxPlcCommunicator（FX 協定解析）。
    /// FxPlcCommunicator 會自動在背景輪詢 X/Y 點位，
    /// 之後透過 GetX(index) 即可讀取最新值。
    ///
    /// 同一次程式執行內第二次以上呼叫（EMO 復歸後重按初始化、流程取消後重新 Init…）：
    /// 一定要先把舊的 _plc Dispose 掉，否則舊的 SerialBytesCommunicator 會一直握著 COM Port，
    /// 新的 SerialPort.Open() 會丟 UnauthorizedAccessException（Access to the path 'COMxx' is denied），
    /// 而且不會自己好，只能重開程式——這是實機踩過的坑。
    /// </summary>
    private DeviceInitResult InitPlc()
    {
        const string name = "PLC (FX)";
        try
        {
            // 先關閉舊連線，釋放 COM Port（第一次呼叫時 _plc 為 null，直接跳過）
            if (_plc != null)
            {
                try { _plc.Dispose(); }
                catch (Exception ex) { _logger.Warn(ex, "[PLC] 關閉舊 PLC 連線時發生例外（忽略，繼續建立新連線）"); }
                _plc = null;
            }

            // 建立串口 Transport → FX 協定通訊器。
            // Dispose 是同步的（會 Join 讀取執行緒再 Close port），理論上回來時 COM Port 已釋放，
            // 但少數 USB-serial 驅動釋放 handle 會慢半拍，所以開埠失敗時短暫等待後重試幾次。
            SerialBytesCommunicator? transport = null;
            const int openRetries = 4;
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    transport = new SerialBytesCommunicator(
                        _plcComPort, _plcBaudRate, 7, Parity.Even, StopBits.One);
                    break;
                }
                catch (UnauthorizedAccessException) when (attempt < openRetries)
                {
                    _logger.Warn($"[PLC] {_plcComPort} 開啟被拒（第 {attempt}/{openRetries} 次），可能舊 handle 尚未釋放，250ms 後重試");
                    Thread.Sleep(250);
                }
            }
            _plc = new FxPlcCommunicator(transport);

            // 等待 PLC 回應（DogValue 會隨每次成功通訊遞增）
            long initialDog = _plc.DogValue;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.Elapsed < _deviceConnectTimeout)
            {
                if (_plc.DogValue != initialDog)
                {
                    _logger.Info($"{name}: OK ({_plcComPort}, DogValue={_plc.DogValue})");
                    _plc.SetY(0, true);
                    _logger.Info("[PLC] Y0 ON (idle)");
                    return DeviceInitResult.Ok(name);
                }
                Thread.Sleep(50);
            }

            return DeviceInitResult.Fail(name,
                $"PLC no response on {_plcComPort} within {_deviceConnectTimeout.TotalSeconds}s");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, $"{name}: FAIL");
            return DeviceInitResult.Fail(name, ex.Message, ex);
        }
    }

    // =========================================
    //  Dry Run (空跑測試)
    // =========================================

    /// <summary>
    /// 空跑測試主流程。
    /// ┌─ STEP 0 : 前置確認（軸狀態、光源）
    /// ├─ STEP 1 : 逐 Slot 迴圈
    /// │   ├─ S1-1 : 光閘檢查（暫停等待）
    /// │   ├─ S1-2 : Y 軸移動到 Slot 位置
    /// │   ├─ S1-3 : 光閘檢查
    /// │   ├─ S1-4 : ZL / ZR 下降至檢測高度
    /// │   ├─ S1-5 : 光閘檢查
    /// │   ├─ S1-6 : 光源 ON → 取像 → 光源 OFF
    /// │   ├─ S1-7 : 光閘檢查（取像完成，不繼續下一張）
    /// │   └─ S1-8 : ZL / ZR 上升至安全高度
    /// ├─ STEP 2 : ZL / ZR 回原點（Y 軸保持原位）
    /// └─ STEP 3 : Y 軸移動至待機位置（496 mm）
    /// </summary>
    public async Task DryRunAsync(
        IProgress<string>? progress = null,
        CancellationToken ct = default,
        string barcode = "",
        IProgress<SlotInspectionProgress>? slotProgress = null)
    {
        // 清除上一次因中斷而殘留的暫停狀態
        _dryRunPaused = false;

        _logger.Info("===== Dry Run Start =====");

        if (SimulationMode)
        {
            await DryRunSimAsync(progress, ct, slotProgress);
            return;
        }

        // ── 週期時間統計：評估總耗時是否超過客戶規定、找出瓶頸階段用（見 finally 最後彙總輸出）──
        var timing = new RunTimingRecorder();
        int timingSlotCount = 0;

        Axes["AxisX"].SetMaxVel(10000);
        Axes["AxisY"].SetMaxVel(10000);
        Axes["AxisZL"].SetMaxVel(6000);
        Axes["AxisZR"].SetMaxVel(6000);

        // ── Y0 閃爍：空跑測試進行中指示燈（1s 亮 / 1s 暗）──
        using var blinkCts = new CancellationTokenSource();
        var blinkTask = Task.Run(async () =>
        {
            bool state = true;
            while (!blinkCts.Token.IsCancellationRequested)
            {
                try
                {
                    _plc?.SetY(0, state);
                    state = !state;
                    await Task.Delay(1000, blinkCts.Token);
                }
                catch (OperationCanceledException) { break; }
                catch { break; }
            }
        });
        _logger.Info("[DryRun] Y0 BLINK start");

        // 光閘（X7）監控統一由 MainViewModel.OnPlcTick 處理，不論是否在 DryRun 期間都會持續輪詢，
        // 這裡不需要另外的背景監控。
        _logger.Info("[DryRun] X7 monitor: 由 OnPlcTick 統一處理");

        try
        {
            // =====================================================
            //  STEP 0 : 前置確認
            // =====================================================

            // STEP 0-0 ? 確保光源已連線
            if (_lightHB == null || !_lightHB.IsOpen)
            {
                progress?.Report("[STEP 0-0] 初始化光源 (HighBright)...");
                var lr = InitLightHB();
                if (!lr.Success)
                    throw new InvalidOperationException($"光源初始化失敗: {lr.Message}");
                _logger.Info("[STEP 0-0] 光源初始化 ?");
            }

            // STEP 0-0 ? 開啟光源（空跑期間持續開啟，結束後才關閉）
            progress?.Report("[STEP 0-0] 光源 CH1+CH2 開啟...");
            bool on1 = LightSetWithRetry(_config.LightChannelLeft,  _config.LightIntensityLeft,  "STEP0-0", "ON");
            bool on2 = LightSetWithRetry(_config.LightChannelRight, _config.LightIntensityRight, "STEP0-0", "ON");
            if (!on1 || !on2)
            {
                string reason = DescribeLightFailureReason(_lightHB?.LastUnexpectedResponse);
                _logger.Warn($"[STEP 0-0] 光源開啟失敗  CH{_config.LightChannelLeft}={on1} CH{_config.LightChannelRight}={on2}，{reason}");
                progress?.Report($"⚠ [STEP 0-0] 光源開啟失敗：{reason}");
            }
            else
            {
                _logger.Info("[STEP 0-0] 光源 CH1+CH2 已開啟 ?");
                progress?.Report("[STEP 0-0] 光源已開啟 ?");
            }

            // STEP 0-1：確認 Y / ZL / ZR 軸已激磁且無警報
            progress?.Report("[STEP 0-1] 確認軸狀態...");
            string[] dryRunAxes = ["AxisY", "AxisZL", "AxisZR"];
            foreach (var axisName in dryRunAxes)
            {
                ct.ThrowIfCancellationRequested();
                if (!Axes.TryGetValue(axisName, out var axis))
                    throw new InvalidOperationException($"{axisName} 不存在");

                if (axis.GetAlarm())
                {
                    progress?.Report($"[STEP 0-1] {axisName} 有警報，嘗試清除...");
                    axis.ResetError();
                    await Task.Delay(500, ct);
                    if (axis.GetAlarm())
                    {
                        Alarms.Raise($"AxisAlarm:{axisName}", $"{axisName} 警報無法清除，請檢查驅動器");
                        throw new InvalidOperationException($"{axisName} 警報無法清除，請檢查驅動器");
                    }
                    Alarms.Clear($"AxisAlarm:{axisName}");
                }

                if (!axis.GetSVON())
                {
                    progress?.Report($"[STEP 0-1] {axisName} 激磁...");
                    axis.SetSVON(true);
                    bool ready = await WaitUntilAsync(
                        () => axis.GetRDY() && !axis.GetAlarm(),
                        _deviceConnectTimeout, ct);
                    if (!ready)
                    {
                        if (axis.GetAlarm())
                            Alarms.Raise($"AxisAlarm:{axisName}", $"{axisName} 激磁失敗，有警報");
                        throw new InvalidOperationException(
                            $"{axisName} 激磁失敗 (RDY={axis.GetRDY()}, Alarm={axis.GetAlarm()})");
                    }
                }

                Alarms.Clear($"AxisAlarm:{axisName}");
                progress?.Report($"[STEP 0-1] {axisName} 就緒 ?");
            }

            // TODO: 相機功能暫時停用
            if (_cameraRight == null || _cameraLeft == null)
            {
                progress?.Report("初始化相機...");
                var cr = await InitCamerasAsync(ct);
                if (!cr.Success)
                    throw new InvalidOperationException($"相機初始化失敗: {cr.Message}");
                progress?.Report("相機 ?");
            }


            _cameraRight.SetFeatureValue(GMExpandParamter.ExposureTime, _config.ExposureTimeRight);
            _cameraLeft.SetFeatureValue(GMExpandParamter.ExposureTime, _config.ExposureTimeLeft);

            // ── 依 X12 / X13 訊號選擇 R組 或 L組 Z 軸高度 ──
            bool boardX12 = GetPlcXOctal(CarrierLeftSensorPlcAddr) ?? false;  // X12 → R組
            bool boardX13 = GetPlcXOctal(CarrierRightSensorPlcAddr) ?? false;  // X13 → L組
            double zCamZL = _config.GetCameraHeightZL(boardX12, boardX13);
            double zCamZR = _config.GetCameraHeightZR(boardX12, boardX13);
            double zSafeZL = _config.GetZLSafeHeight(boardX12, boardX13);
            double zSafeZR = _config.GetZRSafeHeight(boardX12, boardX13);
            string boardGroup = boardX12 ? "R組(X12)" : (boardX13 ? "L組(X13)" : "L組(預設)");
            progress?.Report($"[STEP 0-2] 板子訊號: {boardGroup} → ZL相機高={zCamZL:F2} ZR相機高={zCamZR:F2} ZL安全={zSafeZL:F2} ZR安全={zSafeZR:F2}");
            _logger.Info($"[DryRun] {boardGroup}: ZL={zCamZL:F2}/{zSafeZL:F2}  ZR={zCamZR:F2}/{zSafeZR:F2}");

            // ── 逐 Slot：Y 移動 → ZL/ZR 下降 → 停留 → ZL/ZR 上升 ──
            // Count 動態讀取陣列長度，避免 SlotPositionTable 筆數與此處不同步造成 IndexOutOfRangeException
            var rows = new (SlotInspectionProgress.TargetCollection Target, int Count)[]
        {
            (SlotInspectionProgress.TargetCollection.AreaA_Row1, SlotPositionTable.AreaA_Row1.Length),
            //(SlotInspectionProgress.TargetCollection.AreaA_Row2, SlotPositionTable.AreaA_Row2.Length),
        };

            int totalSlots = rows.Sum(r => r.Count);
            int currentSlot = 0;

            foreach (var (target, count) in rows)
            {
                for (int i = 0; i < count; i=i+1)
                {
                    ct.ThrowIfCancellationRequested();
                    await CheckPauseAsync(progress, ct);
                    currentSlot++;

                    var pos = SlotPositionTable.GetByCarrier(target, i, isR: boardX12);
                    string slotLabel = $"{target} Slot#{i + 1}";
                    string carrierGroup = boardX12 ? "R載台(X12)" : "L載台(X13)";

                    // STEP 1: Y 軸移動到目標 Slot 位置
                    double clampedY = Math.Clamp(pos.Y, 0.0, YAxisMaxMm);
                    _logger.Info($"▶ 開始移動到第 {currentSlot}/{totalSlots} 個 Slot" +
                                 $"（{slotLabel}，{carrierGroup}，Y 目標={clampedY:F2} mm）");
                    progress?.Report($"({currentSlot}/{totalSlots}) {slotLabel}[📦{carrierGroup}]: 正在移動 Y→{clampedY:F1} mm...");
                    var swYMove = System.Diagnostics.Stopwatch.StartNew();
                    await EnsureZSafeBeforeYMoveAsync(zSafeZL, zSafeZR, progress, ct, $"DryRun-Slot{i + 1}");
                    Axes["AxisY"].MotMoveAbs(clampedY);

                    // 等 150ms 讓驅動器接收指令並清除舊的到位旗標，
                    // 避免 Wait() 在 Y 真正移動前因殘留旗標誤回傳 true
                    await Task.Delay(150, ct);

                    bool yArrived = await WaitUntilAsync(
                        () => Axes["AxisY"].Wait(),
                        _config.MoveTimeout, ct);
                    if (!yArrived)
                        throw new TimeoutException($"{slotLabel} Y 軸移動逾時");
                    DryRunCheckAxisState("AxisY", slotLabel);

                    double realY = Axes["AxisY"].GetRealPosition();
                    timing.Add("Y移動", swYMove.ElapsedMilliseconds);
                    _logger.Info($"✔ Slot {i + 1} Y 軸到位" +
                                 $"（目標={clampedY:F2} mm，實際={realY:F2} mm，偏差={(realY - clampedY):+0.00;-0.00;0.00} mm）");
                    progress?.Report($"({currentSlot}/{totalSlots}) {slotLabel}: ✔ Y 軸到位（實際={realY:F2} mm）");

                    // ── 光閘檢查：Y 到位後，若 X7 觸發則暫停 ──
                    await CheckPauseAsync(progress, ct);

                    // STEP 2: ZL + ZR 同時下降至檢測高度（依 X12/X13 選 R組 或 L組 高度）
                    _logger.Info($"▶ Slot {i + 1} ZL/ZR 下降到檢測高度（ZL 目標={zCamZL:F2} mm，ZR 目標={zCamZR:F2} mm，{boardGroup}）");
                    progress?.Report($"({currentSlot}/{totalSlots}) {slotLabel}: ZL ↓{zCamZL:F1} mm 、ZR ↓{zCamZR:F1} mm 下降中...");
                    var swZDown = System.Diagnostics.Stopwatch.StartNew();
                    Axes["AxisZL"].MotMoveAbs(zCamZL);
                    Axes["AxisZR"].MotMoveAbs(zCamZR);

                    // 等 150ms 讓驅動器清除上一次的 InPosition 旗標，避免 Wait() 提早回傳 true
                    await Task.Delay(150, ct);

                    bool zDown = await WaitUntilAsync(
                    () => Axes["AxisZL"].Wait() && Axes["AxisZR"].Wait(),
                    _config.MoveTimeout, ct);
                    if (!zDown)
                        throw new TimeoutException($"{slotLabel} ZL/ZR 下降逾時");
                    DryRunCheckAxisState("AxisZL", slotLabel);
                    DryRunCheckAxisState("AxisZR", slotLabel);

                    double realZL = Axes["AxisZL"].GetRealPosition();
                    double realZR = Axes["AxisZR"].GetRealPosition();
                    timing.Add("Z下降", swZDown.ElapsedMilliseconds);
                    _logger.Info($"✔ Slot {i + 1} ZL/ZR 已到達檢測高度" +
                                 $"（ZL 目標={zCamZL:F2} mm 實際={realZL:F2} mm，" +
                                 $"ZR 目標={zCamZR:F2} mm 實際={realZR:F2} mm）");
                    progress?.Report($"({currentSlot}/{totalSlots}) {slotLabel}: ✔ ZL/ZR 到位（ZL={realZL:F2} mm，ZR={realZR:F2} mm）");

                    // ── 光閘檢查：Z 到位後，若 X7 觸發則暫停 ──
                    await CheckPauseAsync(progress, ct);

                    // STEP 3: ZL/ZR 到位後，在原本 2 秒停留期間執行「開光 → 取像 → 關光」
                    // 左相機 → targetA (AreaA)，右相機 → targetB (AreaB)，共用同一個 slotIndex
                    var targetB = target switch
                    {
                        SlotInspectionProgress.TargetCollection.AreaA_Row1 => SlotInspectionProgress.TargetCollection.AreaB_Row1,
                        SlotInspectionProgress.TargetCollection.AreaA_Row2 => SlotInspectionProgress.TargetCollection.AreaB_Row2,
                        _ => SlotInspectionProgress.TargetCollection.AreaB_Row1
                    };
                    var swCapture = System.Diagnostics.Stopwatch.StartNew();
                    await DryRunCaptureAsync(slotLabel, currentSlot, totalSlots, i, barcode, progress, ct, target, targetB, slotProgress);
                    timing.Add("拍照+分析", swCapture.ElapsedMilliseconds);

                    // ── 光閘檢查：取像完成後，若 X7 觸發則暫停（相機已完成，不繼續下一張）──
                    await CheckPauseAsync(progress, ct);

                    // STEP 4: ZL + ZR 上升恢復安全高度（依 X12/X13 選 R組 或 L組 安全高度）
                    _logger.Info($"▶ Slot {i + 1} ZL/ZR 上升恢復安全高度（ZL 目標={zSafeZL:F2} mm，ZR 目標={zSafeZR:F2} mm）");
                    progress?.Report($"({currentSlot}/{totalSlots}) {slotLabel}: ZL+ZR ↑ 升回安全高度...");
                    var swZUp = System.Diagnostics.Stopwatch.StartNew();
                    Axes["AxisZL"].MotMoveAbs(zSafeZL);
                    Axes["AxisZR"].MotMoveAbs(zSafeZR);

                    // 等 150ms 讓驅動器清除上一次的 InPosition 旗標，避免 Wait() 提早回傳 true
                    await Task.Delay(150, ct);

                    bool zUp = await WaitUntilAsync(
                        () => Axes["AxisZL"].Wait() && Axes["AxisZR"].Wait(),
                        _config.MoveTimeout, ct);
                    if (!zUp)
                        throw new TimeoutException($"{slotLabel} ZL/ZR 上升逾時");
                    DryRunCheckAxisState("AxisZL", slotLabel);
                    DryRunCheckAxisState("AxisZR", slotLabel);

                    double safeRealZL = Axes["AxisZL"].GetRealPosition();
                    double safeRealZR = Axes["AxisZR"].GetRealPosition();
                    timing.Add("Z上升", swZUp.ElapsedMilliseconds);
                    _logger.Info($"✔ Slot {i + 1} ZL/ZR 已恢復安全高度" +
                                 $"（ZL 目標={zSafeZL:F2} mm 實際={safeRealZL:F2} mm，" +
                                 $"ZR 目標={zSafeZR:F2} mm 實際={safeRealZR:F2} mm）");
                    progress?.Report($"({currentSlot}/{totalSlots}) {slotLabel}: ✔ ZL/ZR 安全高度恢復（ZL={safeRealZL:F2} mm，ZR={safeRealZR:F2} mm）");
                    timingSlotCount = currentSlot;
                }
            }

            // =====================================================
            //  STEP 2 : ZL / ZR 回原點（Y 軸保持原位）
            // =====================================================
            progress?.Report("[STEP 2] ZL / ZR 回原點...");
            _logger.Info("[DryRun] STEP 2 Home ZL + ZR");

            string[] dryRunHomeAxes = ["AxisZL", "AxisZR"];
            foreach (var axisName in dryRunHomeAxes)
                if (Axes.TryGetValue(axisName, out var ax)) ax.Home();

            // 與 HomeAllAxesAsync 相同：Home() 下降幅度不受軟體控制，監控實際位置避免撞機
            bool homeDone = await WaitHomeWithSafetyAsync(dryRunHomeAxes, _axisHomeTimeout, ct, BuildZHomeDangerThresholds());
            if (!homeDone)
                _logger.Warn("[STEP 2] ZL/ZR 回原點逾時或下降超出安全範圍，請確認軸狀態");
            else
                progress?.Report("[STEP 2] ZL+ZR 已回原點 ?");

            // =====================================================
            //  STEP 3 : Y 軸移動至待機位置（496 mm）
            // =====================================================
            progress?.Report("[STEP 3] Y 軸移動至待機位置 496 mm...");
            _logger.Info("[DryRun] STEP 3 Y → 496 mm (standby)");

            // 若 X7 觸發暫停，等待恢復後再發出移動指令
            await CheckPauseAsync(progress, ct);
            await EnsureZSafeBeforeYMoveAsync(zSafeZL, zSafeZR, progress, ct, "DryRun-STEP3-Standby");

            while (true)
            {
                ct.ThrowIfCancellationRequested();
                Axes["AxisY"].MotMoveAbs(_config.BarcodePositionY);
                await Task.Delay(150, ct);
                bool yFinal = await WaitUntilAsync(
                    () => Axes["AxisY"].Wait() || _dryRunPaused, _config.MoveTimeout, ct);

                if (_dryRunPaused)
                {
                    // X7 觸發停軸，等待 X10 恢復後重新發出移動指令
                    _logger.Info("[STEP 3] X7 triggered during Y move, waiting for resume...");
                    await CheckPauseAsync(progress, ct);
                    continue; // 重新發出 MotMoveAbs
                }

                if (!yFinal)
                    _logger.Warn("[STEP 3] Y 軸移動至待機位置逾時");
                else
                    progress?.Report($"[STEP 3] Y 已到達 496 mm (實際={Axes["AxisY"].GetRealPosition():F2}) ?");
                break;
            }

            progress?.Report($"空跑測試完成 ?  共跑完 {totalSlots} 個 Slot");
            _logger.Info("===== Dry Run Done =====");
        } // end try
        finally
        {
            // 週期時間統計彙總：無論正常結束或中途發生例外/取消都要輸出，才能評估總耗時
            // 是否超過客戶規定、找出瓶頸階段（只是記錄，不影響任何流程判斷）。
            try { _logger.Info(timing.BuildSummary(timingSlotCount, $"拍照下限延遲{_config.CaptureMinDurationMs}ms")); }
            catch (Exception ex) { _logger.Warn($"[Timing] 彙總輸出失敗：{ex.Message}"); }

            // 無論正常結束、中斷、事件發生，光源一定強制關閉
            if (_lightHB != null && _lightHB.IsOpen)
            {
                try
                {
                    _lightHB.SetValue(_config.LightChannelLeft, 0);
                    _lightHB.SetValue(_config.LightChannelRight, 0);
                    _logger.Info("[DryRun] finally: 光源全關 ?");
                }
                catch (Exception ex)
                {
                    _logger.Warn($"[DryRun] finally: 光源關閉失敗 {ex.Message}");
                }
            }

            // 停止閃爍，Y0 恢復恆亮（idle 狀態）
            blinkCts.Cancel();
            try { await blinkTask; } catch { }
            _plc?.SetY(0, true);
            _logger.Info("[DryRun] Y0 BLINK stop, Y0 ON (idle)");
        }
    }



    /// <summary>
    /// DryRun 期間的光源 + 取像流程（ZL/ZR 到位後的 2 秒停留期間執行）。
    /// 正確順序：Stop（清 IsGrabing）→ 光源 ON → Start → 等完整幀 → Stop（DMA 完成）→ 光源 OFF → GetBufAddress() → 存 BMP。
    /// 影像儲存至 D:\CameraLightTest\yyyyMMdd\  （直接使用 ICamera，不透過 CameraManager）
    /// </summary>
    private async Task DryRunCaptureAsync(
        string slotLabel,
        int currentSlot,
        int totalSlots,
        int slotIndex,
        string barcode ,
        IProgress<string>? progress,
        CancellationToken ct,
        SlotInspectionProgress.TargetCollection targetA = SlotInspectionProgress.TargetCollection.AreaA_Row1,
        SlotInspectionProgress.TargetCollection targetB = SlotInspectionProgress.TargetCollection.AreaB_Row1,
        IProgress<SlotInspectionProgress>? slotProgress = null)
    {

      

        // 影像路徑改由 ImagePathHelper.GetImagePath 統一管理
        int LightChLeft = _config.LightChannelLeft;
        int LightChRight = _config.LightChannelRight;
        int LightPctLeft = _config.LightIntensityLeft;
        int LightPctRight = _config.LightIntensityRight;
        const int StabilizeMs = 100;  // 光源穩定等待（ms）
        // Right 相機 ExposureTime=100000μs(100ms)，需等曝光 + Sapera DMA 傳輸完成
        // 取最長曝光 100ms + 100ms 傳輸緩衝 = 200ms → 再加 100ms 安全餘量 = 300ms
        const int GrabWaitMs = 300;

        // ── 根據 X12 / X13 判斷相機編號與板子側別 ──
        // X12=true → 板子在 R側 → RSlot；Left=C4, Right=C3
        // X13=true → 板子在 L側 → LSlot；Left=C2, Right=C1
        // 連續讀值一致才確認，避免單次 PLC 雜訊讓某個 Slot 誤判到跟其他 Slot 不同側
        (bool x12, bool x13) = await DetermineCarrierSideDebouncedAsync(ct);
        string camLeft = x12 ? "C4" : "C2";   // X12→C4, X13（或預設）→C2
        string camRight = x12 ? "C3" : "C1";   // X12→C3, X13（或預設）→C1
        string slotSide = x12 ? "RSlot" : "LSlot";  // X12→RSlot, X13→LSlot
        _logger.Debug($"[DryRun] {slotLabel} X12={x12} X13={x13} → Left={camLeft} Right={camRight} Side={slotSide}");

        // 本次取像＋分析的細部計時（供 _logger 輸出，找瓶頸用；不影響流程判斷）
        var swGrab = System.Diagnostics.Stopwatch.StartNew();

        // STEP 1: 先停止相機，清除 IsGrabing 狀態
        _cameraRight?.Stop();
        _cameraLeft?.Stop();

        // STEP 2: 檢查光源是否開啟，若未開啟則開啟（防呆：避免中途被意外關閉）
        if (_lightHB != null && _lightHB.IsOpen)
        {
            // 嘗試讀取當前亮度（HighBright 控制器無讀取指令，直接重設確保亮度正確）
            bool ch1On = LightSetWithRetry(LightChLeft, LightPctLeft, slotLabel, "ENSURE_ON");
            bool ch2On = LightSetWithRetry(LightChRight, LightPctRight, slotLabel, "ENSURE_ON");
            if (!ch1On || !ch2On)
                _logger.Warn($"[DryRun] {slotLabel} 光源確認/重設失敗  CH{LightChLeft}={ch1On} CH{LightChRight}={ch2On}，{DescribeLightFailureReason(_lightHB?.LastUnexpectedResponse)}");
        }
        await Task.Delay(StabilizeMs, ct);

        // STEP 3: 開始取像（m_Xfer.Grab()）
        progress?.Report($"({currentSlot}/{totalSlots}) {slotLabel}: 相機取像...");
        _cameraRight?.Start();
        _cameraLeft?.Start();

        // STEP 4: 等待曝光完成 + Sapera DMA 把資料寫入 Buffer
        await Task.Delay(GrabWaitMs, ct);

        // STEP 5: 停止取像（m_Xfer.Abort()）
        _cameraRight?.Stop();
        _cameraLeft?.Stop();

        // STEP 6: 光源保持開啟（不關閉，等空跑流程結束後才關）

        // STEP 7: 讀取 Buffer
        var bufRight = _cameraRight?.GetBufAddress();
        var bufLeft = _cameraLeft?.GetBufAddress();
        swGrab.Stop();
        long analyzeMsR = 0, analyzeMsL = 0;

        // STEP 8: save BMP - Original_image / Draw_image
        // filename: yyyy-MM-ddTHH-mm-ss_{barcode}_{Camera}_{SlotTag}.bmp
        // path managed by ImagePathHelper.GetImagePath
 
        string ts = DateTime.Now.ToString("yyyy-MM-ddTHH-mm-ss");
        // (timestamp defined above)
        string slotNum = (25 - slotIndex).ToString("D2");                 // 25, 26 ...
        string barcodeId = string.IsNullOrWhiteSpace(barcode) ? "UNKNOWN" : barcode;
        string slotTag = $"{slotSide}{slotNum}";                          // LSlot25 or RSlot25
        // 取 slotTag 尾端純數字作為 Layer，例如 LSlot25 → "25"
        string layerNum = System.Text.RegularExpressions.Regex.Match(slotTag, @"\d+$").Value;

        bool anySaved = false;

        // Area A 顯示左相機；Area B 顯示右相機（同一個 Y 位置，各側各自顯示）
        System.Windows.Media.Imaging.BitmapSource? uiImageLeft = null;  // → targetA (AreaA)
        System.Windows.Media.Imaging.BitmapSource? uiImageRight = null;  // → targetB (AreaB)


        // Value 換算係數：pixel² → mm²（1 pixel = 0.0069 mm）
        // DetectedArea 在 BumperAlgService.Analyze() 內已換算為 mm²，此處不再乘係數
        double detectedAreaR = 0.0;
        double detectedAreaL = 0.0;
        bool isNgR = false;
        bool isNgL = false;
        double meanIntensityR = 0.0;
        double meanIntensityL = 0.0;
        double distanceToRoiCenterR = 0.0;
        double distanceToRoiCenterL = 0.0;
        IReadOnlyList<System.Drawing.Rectangle> defectBoxesR = [];
        IReadOnlyList<System.Drawing.Rectangle> defectBoxesL = [];

        if (_cameraRight != null && bufRight != null && bufRight.Length > 0 && bufRight[0] != IntPtr.Zero)
        {
            string path = ImagePathHelper.GetImagePath(barcodeId, $"{ts}_{barcodeId}_{camRight}_{slotTag}.bmp", ImagePathHelper.ImageType.Original);
            DryRunSaveBmp(_cameraRight, bufRight[0], path, _config.GetRoi(camRight));
            _logger.Info($"[DryRun] {slotLabel} 已存 R({camRight}): {path}");
            anySaved = true;

            // 右相機圖片 → AreaB：先呼叫 ALG 畫線，失敗才 fallback 原圖
            string algKeyR = _config.GetAlgJsonKeyFromImagePath(path);
            var slotStartR = DateTime.Now;
            var swAnalyzeR = System.Diagnostics.Stopwatch.StartNew();
            var algR = await _bumperAlg.AnalyzeAsync(path, algKeyR, $"{slotLabel}_R", _config.UseNewBumperAlgorithm);
            analyzeMsR = swAnalyzeR.ElapsedMilliseconds;
            // DetectedArea 已在 BumperAlgService 內換算為 mm²，直接使用
            detectedAreaR = algR.DetectedArea;
            isNgR = detectedAreaR < _config.ValueLimitLow || detectedAreaR > _config.ValueLimitHigh;
            meanIntensityR = algR.MeanIntensity;
            distanceToRoiCenterR = algR.DistanceToRoiCenter;
            defectBoxesR = algR.DefectBoundingBoxes;
            // 寫入 CSV（FoupID=barcode，BumperPosition=camRight，Layer=純數字，Value 已換算）
            InspectionResultWriter.WriteSlotResult(
                barcodeId,
                detectedAreaR,
                isNgR,
                path,
                bumperPosition: camRight,
                layer: layerNum,
                lowerSpec: _config.ValueLimitLow,
                upperSpec: _config.ValueLimitHigh,
                processStartTime: slotStartR);
            if (algR.Success && algR.Image is System.Windows.Media.Imaging.BitmapSource bsR)
            {
                uiImageRight = bsR;
                _logger.Info($"[DryRun] {slotLabel} R ALG 畫線 OK (json={algKeyR}) Area={detectedAreaR:F2}");

                // save draw image via ImagePathHelper
                string drawPath = ImagePathHelper.GetImagePath(barcodeId, $"{ts}_{barcodeId}_{camRight}_{slotTag}_draw.bmp", ImagePathHelper.ImageType.Draw);
                SaveBitmapSource(bsR, drawPath);
 
                _logger.Info($"[DryRun] {slotLabel} R 畫線圖已存: {drawPath}");
            }
            else
            {
                _logger.Warn($"[DryRun] {slotLabel} R ALG 失敗({algR.Message})，顯示原圖");
                uiImageRight = BufferToBitmapSource(_cameraRight, bufRight[0], _config.GetRoi(camRight));
            }
        }
        if (_cameraLeft != null && bufLeft != null && bufLeft.Length > 0 && bufLeft[0] != IntPtr.Zero)
        {
            string path = ImagePathHelper.GetImagePath(barcodeId, $"{ts}_{barcodeId}_{camLeft}_{slotTag}.bmp", ImagePathHelper.ImageType.Original);
            DryRunSaveBmp(_cameraLeft, bufLeft[0], path, _config.GetRoi(camLeft));
            _logger.Info($"[DryRun] {slotLabel} 已存 L({camLeft}): {path}");
            anySaved = true;

            // 左相機圖片 → AreaA：先呼叫 ALG 畫線，失敗才 fallback 原圖
            string algKeyL = _config.GetAlgJsonKeyFromImagePath(path);
            var slotStartL = DateTime.Now;
            var swAnalyzeL = System.Diagnostics.Stopwatch.StartNew();
            var algL = await _bumperAlg.AnalyzeAsync(path, algKeyL, $"{slotLabel}_L", _config.UseNewBumperAlgorithm);
            analyzeMsL = swAnalyzeL.ElapsedMilliseconds;
            detectedAreaL = algL.DetectedArea;
            isNgL = detectedAreaL < _config.ValueLimitLow || detectedAreaL > _config.ValueLimitHigh;
            meanIntensityL = algL.MeanIntensity;
            distanceToRoiCenterL = algL.DistanceToRoiCenter;
            defectBoxesL = algL.DefectBoundingBoxes;
            // 寫入 CSV（FoupID=barcode，BumperPosition=camLeft，Layer=純數字，Value 已換算）
            InspectionResultWriter.WriteSlotResult(
                barcodeId,
                detectedAreaL,
                isNgL,
                path,
                bumperPosition: camLeft,
                layer: layerNum,
                lowerSpec: _config.ValueLimitLow,
                upperSpec: _config.ValueLimitHigh,
                processStartTime: slotStartL);
            if (algL.Success && algL.Image is System.Windows.Media.Imaging.BitmapSource bsL)
            {
                uiImageLeft = bsL;
                _logger.Info($"[DryRun] {slotLabel} L ALG 畫線 OK (json={algKeyL}) Area={detectedAreaL:F2}");

                // save draw image via ImagePathHelper
                string drawPath = ImagePathHelper.GetImagePath(barcodeId, $"{ts}_{barcodeId}_{camLeft}_{slotTag}_draw.bmp", ImagePathHelper.ImageType.Draw);
                SaveBitmapSource(bsL, drawPath);
 
                _logger.Info($"[DryRun] {slotLabel} L 畫線圖已存: {drawPath}");
            }
            else
            {
                _logger.Warn($"[DryRun] {slotLabel} L ALG 失敗({algL.Message})，顯示原圖");
                uiImageLeft = BufferToBitmapSource(_cameraLeft, bufLeft[0], _config.GetRoi(camLeft));
            }
        }

        // 即時更新 UI 圖像：左相機 → AreaA，右相機 → AreaB
        if (slotProgress != null)
        {
            string statusText = $"({currentSlot}/{totalSlots}) {slotLabel}: 取像完成 ?";

            if (uiImageLeft != null)
            {
                slotProgress.Report(new SlotInspectionProgress
                {
                    Target = targetA,
                    SlotIndex = slotIndex,
                    Value = detectedAreaL,
                    IsNg = isNgL,
                    MeanIntensity = meanIntensityL,
                    DistanceToRoiCenter = distanceToRoiCenterL,
                    DefectBoundingBoxes = defectBoxesL,
                    Image = uiImageLeft,
                    StatusText = statusText
                });
            }

            if (uiImageRight != null)
            {
                slotProgress.Report(new SlotInspectionProgress
                {
                    Target = targetB,
                    SlotIndex = slotIndex,
                    Value = detectedAreaR,
                    IsNg = isNgR,
                    MeanIntensity = meanIntensityR,
                    DistanceToRoiCenter = distanceToRoiCenterR,
                    DefectBoundingBoxes = defectBoxesR,
                    Image = uiImageRight,
                    StatusText = statusText
                });
            }
        }

        if (!anySaved)
        {
            _logger.Warn($"[DryRun] {slotLabel} 取像失敗（兩鏡頭皆無影像）");
            progress?.Report($"({currentSlot}/{totalSlots}) {slotLabel}: ? 取像失敗");
        }
        else
        {
            progress?.Report($"({currentSlot}/{totalSlots}) {slotLabel}: images saved");
        }

        // STEP 9: 補足剩餘停留時間（確保整體 ≥ _config.CaptureMinDurationMs，預設 2000ms）。
        // 原本寫死 2000，改成可調整參數（InspectionConfig.CaptureMinDurationMs），
        // 方便在機台上實測調降、評估對週期時間的影響。
        int captureMinDurationMs = _config.CaptureMinDurationMs;
        int remaining = captureMinDurationMs - StabilizeMs - GrabWaitMs;
        if (remaining > 0)
            await Task.Delay(remaining, ct);

        // 細部計時：取像(硬體，含 StabilizeMs+GrabWaitMs 固定延遲) / 分析R+L / 強制補滿延遲，
        // 用來看清楚「下限」裡有多少是真正在做事、多少是寫死的等待時間。
        _logger.Info($"[Timing] {slotLabel} 拍照+分析細部：取像{swGrab.ElapsedMilliseconds}ms、" +
                     $"分析R+L共{analyzeMsR + analyzeMsL}ms（R={analyzeMsR}ms,L={analyzeMsL}ms）、" +
                     $"強制補滿延遲{Math.Max(0, remaining)}ms（下限設定{captureMinDurationMs}ms）");
    }

    /// <summary>將 BitmapSource 存成 BMP 檔（用於儲存 ALG 畫線後的影像）。</summary>
    private void SaveBitmapSource(System.Windows.Media.Imaging.BitmapSource bitmap, string filePath)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write);
            var encoder = new System.Windows.Media.Imaging.BmpBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            encoder.Save(fs);
        }
        catch (Exception ex)
        {
            _logger.Warn($"[DryRun] SaveBitmapSource 失敗 ({filePath}): {ex.Message}");
        }
    }

    /// <summary>DryRun 專用：從 ICamera Buffer 存成 BMP 檔，支援裁切 ROI。</summary>
    private void DryRunSaveBmp(ICamera camera, IntPtr bufPtr, string filePath, CameraRoi? roi = null)
    {
        try
        {
            int w = camera.FrameWidth;
            int h = camera.BufHeight;
            int stride = w * camera.PixelBytes;
            var fmt = camera.PixelBytes == 3
                ? System.Windows.Media.PixelFormats.Rgb24
                : System.Windows.Media.PixelFormats.Gray8;

            var fullBmp = System.Windows.Media.Imaging.BitmapSource.Create(
                w, h, 96, 96, fmt, null, bufPtr, h * stride, stride);
            fullBmp.Freeze();

            System.Windows.Media.Imaging.BitmapSource saveBmp = fullBmp;

            // ── 裁切 ROI（邊界保護：超出範圍則不裁切）──
            if (roi != null && roi.IsEnabled
                && (roi.X + roi.W) <= w
                && (roi.Y + roi.H) <= h)
            {
                var rect = new System.Windows.Int32Rect(roi.X, roi.Y, roi.W, roi.H);
                var cropped = new System.Windows.Media.Imaging.CroppedBitmap(fullBmp, rect);
                cropped.Freeze();
                saveBmp = cropped;
                _logger.Debug($"[DryRun] SaveBmp 裁切 ROI=({roi.X},{roi.Y}) {roi.W}×{roi.H} → {filePath}");
            }

            using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write);
            var encoder = new System.Windows.Media.Imaging.BmpBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(saveBmp));
            encoder.Save(fs);
        }
        catch (Exception ex)
        {
            _logger.Warn($"[DryRun] SaveBmp 失敗 ({filePath}): {ex.Message}");
        }
    }

    /// <summary>從 ICamera Buffer 轉成 WPF BitmapSource（供 UI 即時顯示），支援裁切 ROI。</summary>
    private System.Windows.Media.Imaging.BitmapSource? BufferToBitmapSource(ICamera camera, IntPtr bufPtr, CameraRoi? roi = null)
    {
        try
        {
            int w = camera.FrameWidth;
            int h = camera.BufHeight;
            int stride = w * camera.PixelBytes;
            var fmt = camera.PixelBytes == 3
                ? System.Windows.Media.PixelFormats.Rgb24
                : System.Windows.Media.PixelFormats.Gray8;

            var fullBmp = System.Windows.Media.Imaging.BitmapSource.Create(
                w, h, 96, 96, fmt, null, bufPtr, h * stride, stride);
            fullBmp.Freeze();

            if (roi != null && roi.IsEnabled
                && (roi.X + roi.W) <= w
                && (roi.Y + roi.H) <= h)
            {
                var rect = new System.Windows.Int32Rect(roi.X, roi.Y, roi.W, roi.H);
                var cropped = new System.Windows.Media.Imaging.CroppedBitmap(fullBmp, rect);
                cropped.Freeze();
                return cropped;
            }

            return fullBmp;
        }
        catch (Exception ex)
        {
            _logger.Warn($"[DryRun] BufferToBitmapSource 失敗: {ex.Message}");
            return null;
        }
    }

    /// <summary>空跑：檢查軸狀態（警報/極限），有問題就拋例外或記 log</summary>
    private void DryRunCheckAxisState(string axisName, string context)
    {
        var axis = Axes[axisName];
        if (axis.GetAlarm())
        {
            Alarms.Raise($"AxisAlarm:{axisName}", $"{axisName} 於 {context} 發生警報");
            throw new InvalidOperationException($"{axisName} 於 {context} 發生警報");
        }
        Alarms.Clear($"AxisAlarm:{axisName}");
        if (axis.GetPLimit())
            _logger.Warn($"[DryRun] {axisName} 正極限觸發 ({context})");
        if (axis.GetNLimit())
            _logger.Warn($"[DryRun] {axisName} 負極限觸發 ({context})");
    }

    /// <summary>
    /// 依 HighBright 最近一次非預期回應，組出操作員看得懂的具體原因。
    /// 目前已知：
    /// 1. 控制器面板被切到「VR Mode」（本機旋鈕手動控制）時，會拒絕所有 RS-232 遠端調光指令，
    ///    此時軟體端不管送 0% 還是 100% 都不會生效，必須先在控制器面板切回遠端/RS232 控制模式。
    /// 2. 控制器對指令持續回應通用錯誤碼「E」（有時混雜殘缺回應，例如 "1,&lt;"），且 COM Port
    ///    本身並未斷線（IsOpen 仍是 true）——實測過重開軟體/重連 COM Port 都無法解決，
    ///    必須把控制器「實體斷電重開」才會恢復，研判是控制器內部卡住而非通訊參數問題。
    /// </summary>
    private static string DescribeLightFailureReason(string? lastUnexpectedResponse)
    {
        if (!string.IsNullOrEmpty(lastUnexpectedResponse) &&
            lastUnexpectedResponse.Contains("VR Mode", StringComparison.OrdinalIgnoreCase))
        {
            return "光源控制器目前為面板手動模式（VR Mode），已拒絕遠端調光指令，請至控制器面板切換為遠端/RS232 控制模式後再試";
        }
        if (!string.IsNullOrEmpty(lastUnexpectedResponse))
        {
            return $"光源控制器回應錯誤（{lastUnexpectedResponse}），COM Port 本身仍連線中，重開軟體無效——請將光源控制器「實體斷電重開」後再試";
        }
        return "請確認光源控制器連線";
    }

    /// <summary>
    /// 光源設值（含 retry 與詳細 Log）。
    /// HighBright 控制器有時因串口回應慢而 Timeout，最多重試 2 次。
    /// </summary>
    private bool LightSetWithRetry(int channel, int percent, string context, string action, int maxRetry = 2)
    {
        if (_lightHB == null || !_lightHB.IsOpen)
        {
            _logger.Error($"[Light] {context} CH{channel} {action} 失敗：光源未連線");
            return false;
        }

        for (int attempt = 1; attempt <= maxRetry; attempt++)
        {
            try
            {
                bool ok = _lightHB.SetValue(channel, percent);
                if (ok)
                {
                    _logger.Info($"[Light] {context} CH{channel} {action} {percent}% → OK (attempt {attempt})");
                    return true;
                }
                _logger.Warn($"[Light] {context} CH{channel} {action} {percent}% → 回應錯誤 (attempt {attempt}/{maxRetry})  resp=\"{_lightHB.LastUnexpectedResponse}\"");
            }
            catch (Exception ex)
            {
                _logger.Error($"[Light] {context} CH{channel} {action} {percent}% → 例外 (attempt {attempt}/{maxRetry}): {ex.Message}");
            }

            if (attempt < maxRetry)
                Thread.Sleep(50); // 短暫等待後重試
        }

        return false;
    }

    /// <summary>
    /// 立即將亮度設定值送到已連線的 HighBright 光源控制器（亮度設定視窗按 OK 時呼叫），
    /// 讓操作員能馬上肉眼確認實際變化，不必等到下一次 S03/DryRun 開燈才套用（避免「調了沒反應」）。
    /// 模擬模式下沒有硬體可套用，視為成功但會在 message 註明。
    /// </summary>
    public bool TryApplyLightIntensityNow(int leftPercent, int rightPercent, out string message)
    {
        if (SimulationMode)
        {
            message = "模擬模式，未實際送出光源指令";
            return true;
        }

        if (_lightHB == null || !_lightHB.IsOpen)
        {
            // Devices Init（InitializeDevicesAsync）不會連光源，只有跑過 DryRun/Run 才會順便連上；
            // 測試按鈕的目的就是不用先跑 Run 才能測，所以這裡自己嘗試連線一次，而不是直接回報失敗。
            var lightInit = InitLightHB();
            if (!lightInit.Success)
            {
                message = $"光源控制器連線失敗（{_lightComPort}）：{lightInit.Message}";
                return false;
            }
        }

        bool okL = LightSetWithRetry(_config.LightChannelLeft, leftPercent, "LightSettings", "SET");
        bool okR = LightSetWithRetry(_config.LightChannelRight, rightPercent, "LightSettings", "SET");

        if (okL && okR)
        {
            message = "已即時套用至光源硬體";
            return true;
        }

        string reason = DescribeLightFailureReason(_lightHB.LastUnexpectedResponse);
        message = $"光源指令未成功送達（CH{_config.LightChannelLeft}={(okL ? "OK" : "失敗")}, CH{_config.LightChannelRight}={(okR ? "OK" : "失敗")}）：{reason}";
        return false;
    }

    /// <summary>模擬模式的空跑：用 delay 模擬 ZL/ZR 升降 + Y 移動</summary>
    private async Task DryRunSimAsync(
        IProgress<string>? progress,
        CancellationToken ct,
        IProgress<SlotInspectionProgress>? slotProgress = null)
    {
        // 改用 InspectionConfig.SimImageFolderPath（可在UI設定），不再寫死路徑，
        // 才能跟 InspectOneSlotAsync 的模擬分支（同樣讀 SimImageFolderPath）保持一致。
        string simImageRoot = _config.SimImageFolderPath;
        // 圖檔 prefix：模擬資料夾下需用 AreaA_Row1_Slot#N_L/R 命名
        const string ImgPrefix = "AreaA_Row1";
        const int SlotCount = 25;

        if (!Directory.Exists(simImageRoot))
        {
            _logger.Warn($"[DryRun-Sim] 模擬影像資料夾不存在：{simImageRoot}（設定於 座標設定/InspectionConfig.SimImageFolderPath），將顯示空白影像");
            progress?.Report($"⚠ 模擬影像資料夾不存在：{simImageRoot}，將顯示空白影像");
        }
        else if (Directory.GetFiles(simImageRoot, $"{ImgPrefix}_Slot#*_*_*.bmp").Length == 0)
        {
            _logger.Warn($"[DryRun-Sim] 模擬影像資料夾內找不到符合命名規則的圖檔：{simImageRoot}（預期格式：{ImgPrefix}_Slot#N_L或R_*.bmp），將顯示空白影像");
            progress?.Report($"⚠ 模擬影像資料夾內找不到符合命名規則的圖檔，將顯示空白影像");
        }

        progress?.Report($"[Sim] 開始模擬取像，共 {SlotCount} 個 Slot");
        var simSw = System.Diagnostics.Stopwatch.StartNew();

        for (int i = 0; i < SlotCount; i++)
        {
            ct.ThrowIfCancellationRequested();
            int slotNum = 25 - i; // 遞減：25, 24, ..., 1

            progress?.Report($"[Sim] ({i + 1}/{SlotCount}) SLOT#{slotNum}: 模擬取像...");

            // _L → AreaA（左相機），_R → AreaB（右相機），同一個 slot 同時回報
            System.Windows.Media.ImageSource? imgL = LoadSimImage(simImageRoot, ImgPrefix, slotNum, "L");
            System.Windows.Media.ImageSource? imgR = LoadSimImage(simImageRoot, ImgPrefix, slotNum, "R");

            string statusText = $"[Sim] SLOT#{slotNum} 取像完成";

            // AreaA：左相機圖
            slotProgress?.Report(new SlotInspectionProgress
            {
                Target     = SlotInspectionProgress.TargetCollection.AreaA_Row1,
                SlotIndex  = i,
                Image      = imgL,
                StatusText = statusText,
            });

            // 給 UI 執行緒時間處理 AreaA 再投遞 AreaB
            await Task.Delay(10, ct);

            // AreaB：右相機圖（同步顯示）
            slotProgress?.Report(new SlotInspectionProgress
            {
                Target     = SlotInspectionProgress.TargetCollection.AreaB_Row1,
                SlotIndex  = i,
                Image      = imgR,
                StatusText = statusText,
            });

            await Task.Delay(290, ct); // 模擬每槽間隔（總計 300ms）
        }

        progress?.Report($"[Sim] 模擬完成，共 {SlotCount} 個 Slot");
        // 模擬模式的延遲都是寫死的假延遲（每 Slot 固定 300ms），這個總時間僅供驗證「計時功能有沒有正常
        // 記錄並輸出」，不代表真實機台的週期時間，真正要評估對客戶規定的時間得在實機（DryRunAsync）看。
        _logger.Info($"[Timing][SIM-僅供功能驗證，非真實週期時間] 模擬空跑總耗時 {simSw.ElapsedMilliseconds / 1000.0:F1}s（{SlotCount} 個 Slot）");
        _logger.Info("===== Dry Run (Sim) Done =====");
    }

    /// <summary>
    /// 從 SimImageRoot 找到符合 &lt;prefix&gt;_Slot#&lt;N&gt;_&lt;side&gt;_*.bmp 的最新檔案並載入為 BitmapSource。
    /// </summary>
    private static System.Windows.Media.ImageSource? LoadSimImage(string root, string prefix, int slotNum, string side)
    {
        if (!System.IO.Directory.Exists(root))
            return null;

        var pattern = $"{prefix}_Slot#{slotNum}_{side}_*.bmp";
        var files = System.IO.Directory.GetFiles(root, pattern);
        if (files.Length == 0)
            return null;

        // 取最後修改時間最新的一張
        var file = files.OrderByDescending(f => System.IO.File.GetLastWriteTime(f)).First();

        try
        {
            var bi = new System.Windows.Media.Imaging.BitmapImage();
            bi.BeginInit();
            bi.UriSource   = new Uri(file, UriKind.Absolute);
            bi.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bi.EndInit();
            bi.Freeze();
            return bi;
        }
        catch (Exception ex)
        {
            _logger.Warn($"[DryRun-Sim] 讀取模擬圖檔失敗：{file}，{ex.Message}");
            return null;
        }
    }

    //  Utility
    // =========================================

    public static async Task<bool> WaitUntilAsync(
        Func<bool> condition, TimeSpan timeout,
        CancellationToken ct = default, int pollIntervalMs = 100)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            while (!condition())
            {
                cts.Token.ThrowIfCancellationRequested();
                await Task.Delay(pollIntervalMs, cts.Token);
            }
            return true;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return false; }
    }

    // =========================================
    //  Dispose
    // =========================================

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (!SimulationMode)
        {
            // Stop and release cameras
            try
            {
                _cameraManager?.CameraeStop(_cameraKeyLeft);
                _cameraManager?.CameraeStop(_cameraKeyRight);
            }
            catch { }

            try { _barcodeDevice?.Dispose(); } catch { }
            try
            {
                _plc?.SetY(0, false);
                _logger.Info("[Dispose] Y0 OFF");
            }
            catch { }
            try { _plc?.Dispose(); } catch { }
            try { _lightHB?.Dispose(); } catch { }

            foreach (var axis in Axes.Values)
                try { axis.MotStop(); axis.SetSVON(false); } catch { }
        }

        // AI演算法(BumperFlatDetection)的模型即使在模擬模式下也可能被觸發載入，disposal 不受 SimulationMode 限制
        try { _bumperAlg.DisposeNewDetector(); } catch { }

        _logger.Info("MachineController Disposed");
    }
}
