using BarcodeReader.Interfaces;
using BarcodeReader.Services;
using Emgu.CV.XFeatures2D;
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
    private const string CameraUidLeft  = "Left";
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

    private FoupInspecMachine.Models.OPT_Controller? _light;
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

    private readonly InspectionConfig _config = new();
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
        if (_plc == null || !_plc.IsConnected) return false;
        return _plc.GetX(index);
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
        if (_plc == null || !_plc.IsConnected) return null;
        return _plc.GetX(OctalAddrToIndex(plcOctalAddr));
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
            place  *= 8;
            plcOctalAddr /= 10;
        }
        return result;
    }

    /// <summary>載台在席感測器 PLC X 點位（八進制位址）</summary>
    private const int CarrierLeftSensorPlcAddr  = 12;   // X12（八進制）→ OctalAddrToIndex = 10
    private const int CarrierRightSensorPlcAddr = 13;   // X13（八進制）→ OctalAddrToIndex = 11

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
        bool left  = GetPlcXOctal(CarrierLeftSensorPlcAddr)  ?? false;   // X12（八進制）→ index 10
        bool right = GetPlcXOctal(CarrierRightSensorPlcAddr) ?? false;  // X13（八進制）→ index 11

        var position = (left, right) switch
        {
            (true, true)   => CarrierPosition.Both,
            (true, false)  => CarrierPosition.Left,
            (false, true)  => CarrierPosition.Right,
            (false, false) => CarrierPosition.None,
        };

        _logger.Info($"[S01] PLC sensor: X12(Left)={left}, X13(Right)={right} → {position}");
        return position;
    }

    /// <summary>
    /// S01b：依載台位置移動軸到讀碼位置 → 軟體觸發讀碼器 → 回傳條碼字串。
    /// 失敗回傳 null。
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

        // STEP 1: 只有 X 軸移動到讀碼位置
        double targetX = _config.GetBarcodePositionX(position);
        progress?.Report($"X 軸移動至讀碼位置（{position}）...");
        _logger.Info($"[S01] Moving AxisX to barcode pos: {position} → X={targetX}");

        Axes["AxisX"].MotMoveAbs(targetX);

        bool arrived = await WaitUntilAsync(
            () => Axes["AxisX"].Wait(),
            _config.MoveTimeout, ct);

        if (!arrived)
        {
            _logger.Warn("[S01] Move to barcode position timeout");
            progress?.Report("移動至讀碼位置逾時");
            return null;
        }

        // STEP 2: 軟體觸發讀碼器取像 + 解析條碼
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

        // progress?.Report("Init light...");
        // result.Add(InitLightHB());
        // if (!result.AllPassed) return result;

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

        // Row1: AreaA_Row1 + AreaB_Row1 同時處理（13 slots）
        // Row2: AreaA_Row2 + AreaB_Row2 同時處理（12 slots）
        var rows = new (SlotInspectionProgress.TargetCollection AreaA,
                        SlotInspectionProgress.TargetCollection AreaB,
                        int Count)[]
        {
            (SlotInspectionProgress.TargetCollection.AreaA_Row1,
             SlotInspectionProgress.TargetCollection.AreaB_Row1, 13),
            (SlotInspectionProgress.TargetCollection.AreaA_Row2,
             SlotInspectionProgress.TargetCollection.AreaB_Row2, 12),
        };


        Axes["AxisY"].SetMaxVel(3000);
        Axes["AxisZL"].SetMaxVel(2000);
        Axes["AxisZR"].SetMaxVel(2000);




        foreach (var (areaA, areaB, count) in rows)
        {
            for (int i = 0; i < count; i++)
            {
                ct.ThrowIfCancellationRequested();

                // 同時對 AreaA 和 AreaB 進行檢測
                var taskA = InspectOneSlotAsync(barcode, areaA, i, ct);
                var taskB = InspectOneSlotAsync(barcode, areaB, i, ct);
                await Task.WhenAll(taskA, taskB);

                var (valueA, isNgA, imagePathA, imageA) = taskA.Result;
                var (valueB, isNgB, imagePathB, imageB) = taskB.Result;

                // 寫入結果
                string slotNameA = $"{areaA}_Slot{i + 1}";
                string slotNameB = $"{areaB}_Slot{i + 1}";
                InspectionResultWriter.WriteSlotResult(barcode, slotNameA, valueA, isNgA, imagePathA);
                InspectionResultWriter.WriteSlotResult(barcode, slotNameB, valueB, isNgB, imagePathB);

                // 同時回報 AreaA 和 AreaB 的進度（UI 同時顯示兩張圖）
                progress?.Report(new SlotInspectionProgress
                {
                    Target     = areaA,
                    SlotIndex  = i,
                    Value      = valueA,
                    IsNg       = isNgA,
                    StatusText = $"[{areaA}] Slot {i + 1}/{count} - {(isNgA ? "NG" : "OK")}",
                    Image      = imageA
                });

                progress?.Report(new SlotInspectionProgress
                {
                    Target     = areaB,
                    SlotIndex  = i,
                    Value      = valueB,
                    IsNg       = isNgB,
                    StatusText = $"[{areaB}] Slot {i + 1}/{count} - {(isNgB ? "NG" : "OK")}",
                    Image      = imageB
                });

                _logger.Debug($"[S03] {areaA}[{i}] Value={valueA:F2} IsNg={isNgA}");
                _logger.Debug($"[S03] {areaB}[{i}] Value={valueB:F2} IsNg={isNgB}");
            }
        }

        _logger.Info($"===== S03 Inspection Done | Barcode: {barcode} =====");
    }

    /// <summary>
    /// Single Slot inspection (7 steps):
    ///   Move axis -> Light on -> Capture -> Save -> Light off -> Measure -> NG judge
    /// Simulation: bypass all hardware, generate fake data + test image.
    /// </summary>
    private async Task<(double value, bool isNg, string imagePath, System.Windows.Media.ImageSource? image)>
        InspectOneSlotAsync(
            string barcode,
            SlotInspectionProgress.TargetCollection target,
            int slotIndex,
            CancellationToken ct)
    {
        // ==================================
        //  Simulation mode
        // ==================================
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

                var algResult = await Task.Run(
                    () => _bumperAlg.Analyze(simPath, jsonKey, slotLabel), ct);

                if (algResult.Success && algResult.Image != null)
                {
                    double simValueOk = algResult.IsNg ? 0.0 : 1.0;
                    _logger.Debug($"[S03-Sim] {slotLabel} ALG OK, json={jsonKey}, isNg={algResult.IsNg}");
                    return (simValueOk, algResult.IsNg, simPath, algResult.Image);
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
                    return (1.0, false, simPath, fallbackImage);
                }
            }

            // 最終 fallback: 無圖可用時才用生成假圖
            var rng = new Random();
            double simL = Math.Round(0.45 + rng.NextDouble() * 0.1, 2);
            double simR = Math.Round(0.45 + rng.NextDouble() * 0.1, 2);
            double simValue = (simL + simR) / 2.0;
            bool simNg = simValue < _config.NgThresholdLow
                      || simValue > _config.NgThresholdHigh;

            System.Windows.Media.ImageSource? simImage = SimImageGenerator.Generate(slotLabel, simValue, simNg);
            return (simValue, simNg, "", simImage);
        }

        // ==================================
        //  Real mode: 7 steps
        // ==================================

        string slotName = $"{target}_Slot{slotIndex + 1}";
        _logger.Debug($"[S03] Start measuring {slotName}");

        // STEP 1: Y 軸移動載台到 Slot 位置，ZL/ZR 調整相機高度
        var pos = SlotPositionTable.Get(target, slotIndex);
        Axes["AxisY"].MotMoveAbs(pos.Y);
        Axes["AxisZL"].MotMoveAbs(_config.CameraHeightZL);
        Axes["AxisZR"].MotMoveAbs(_config.CameraHeightZR);

        bool arrived = await WaitUntilAsync(
            () => Axes["AxisY"].Wait() && Axes["AxisZL"].Wait() && Axes["AxisZR"].Wait(),
            _config.MoveTimeout, ct);

        if (!arrived)
            _logger.Warn($"[S03] {slotName} move timeout");

        // STEP 2: Both lights ON simultaneously
        // TODO: 拍照存圖暫時停用
        // _light!.SetValue(_config.LightChannelLeft,  _config.LightIntensityLeft);
        // _light.SetValue(_config.LightChannelRight, _config.LightIntensityRight);
        // await Task.Delay(_config.LightStabilizeMs, ct);

        // STEP 3: Both cameras trigger simultaneously
        //_cameraManager!.CameraStart(_cameraKeyLeft);
        //_cameraManager.CameraStart(_cameraKeyRight);
        //await Task.Delay(_config.CaptureWaitMs, ct);

        // STEP 4: Get both images
        //var imageLeft  = _cameraManager.GetCameraImage(_cameraKeyLeft);
        //var imageRight = _cameraManager.GetCameraImage(_cameraKeyRight);

        // STEP 5: Both lights OFF
        // _light.SetValue(_config.LightChannelLeft,  0);
        // _light.SetValue(_config.LightChannelRight, 0);

        // Capture failure check
        //if (imageLeft == null || imageRight == null)
        //{
        //    string failSide = imageLeft == null && imageRight == null ? "BOTH"
        //                    : imageLeft == null ? "LEFT" : "RIGHT";
        //    _logger.Warn($"[S03] {slotName} {failSide} capture failed, treat as NG");
        //    return (0, true, "", null);
        //}

        // STEP 6: Save images
        string imagePath = "";
        string pathL = "", pathR = "";
        //if (_config.SaveImages)
        //{
        //    string dir = Path.Combine(
        //        _config.ImageSavePath,
        //        DateTime.Now.ToString("yyyyMMdd"),
        //        barcode);
        //    Directory.CreateDirectory(dir);

        //    pathL = Path.Combine(dir, $"{slotName}_L.tif");
        //    pathR = Path.Combine(dir, $"{slotName}_R.tif");

        //    _cameraManager.SaveCameraImage(_cameraKeyLeft,  pathL, barcode, $"{slotName}_L");
        //    _cameraManager.SaveCameraImage(_cameraKeyRight, pathR, barcode, $"{slotName}_R");

        //    imagePath = pathL;
        //}

        // STEP 7: 呼叫 BumperFlat 演算法分析（使用已存檔的 .tif 路徑）
        string algJsonKey = _config.GetAlgJsonKey(target, slotIndex);

        var algResultL = await Task.Run(() => _bumperAlg.Analyze(pathL, algJsonKey, $"{slotName}_L"), ct);
        var algResultR = await Task.Run(() => _bumperAlg.Analyze(pathR, algJsonKey, $"{slotName}_R"), ct);

        _logger.Debug($"[S03] {slotName} ALG L={algResultL.Message} R={algResultR.Message}");

        // STEP 8: NG 判斷（左右任一 NG 即為 NG）
        bool isNg = algResultL.IsNg || algResultR.IsNg;
        double measuredValue = isNg ? 0.0 : 1.0;

        // 優先顯示左側疊加結果影像
        System.Windows.Media.ImageSource? displayImage = algResultL.Image ?? algResultR.Image;

        return (measuredValue, isNg, imagePath, displayImage);
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

    private DeviceInitResult InitLight()
    {
        const string name = "OPT Light";
        try
        {
            _light = new FoupInspecMachine.Models.OPT_Controller(_lightComPort);
            _light.Open();
            if (!_light.IsOpen) return DeviceInitResult.Fail(name, $"Cannot open {_lightComPort}");
            _ = _light.GetValue(1);
            _logger.Info($"{name}: OK ({_lightComPort})"); return DeviceInitResult.Ok(name);
        }
        catch (Exception ex) { _logger.Error(ex, $"{name}: FAIL"); return DeviceInitResult.Fail(name, $"{_lightComPort}: {ex.Message}", ex); }
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
                if (axis is cAxis_RS485 rs485Axis)
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
                    return DeviceInitResult.Fail(name,
                        $"{axisName} not ready (RDY={axis.GetRDY()}, Alarm={axis.GetAlarm()})");
                _logger.Debug($"{axisName} ServoOn OK");
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
            bool nonYDone = await WaitHomeWithSafetyAsync(nonYAxes, _axisHomeTimeout, ct);
            if (!nonYDone)
                return DeviceInitResult.Fail(name, $"X/ZL/ZR Home timeout");

            // ── Step 2：再 home Y ──
            progress?.Report("Y 軸歸原點...");
            if (!Axes.TryGetValue("AxisY", out var axisY))
                return DeviceInitResult.Fail(name, "AxisY 不存在");
            if (axisY is cAxis_RS485 yRs485)
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
                    return DeviceInitResult.Fail(name, $"{axisName} alarm after home");
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
    /// 等待所有軸完成原點賦歸，並同時監控 X7 光閘訊號。
    /// X7=0 → 立即停軸 → 等待 X10=1（上升邊緣）→ 重新發出 Home 指令 → 繼續等待。
    /// </summary>
    private async Task<bool> WaitHomeWithSafetyAsync(string[] nonYAxes, TimeSpan  HomeTimeout,  CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        const int DebounceCount = 3;
        int x7LowCount = 0;

        while (sw.Elapsed < _axisHomeTimeout)
        {
            ct.ThrowIfCancellationRequested();

            // X7 光閘偵測（暫時停用）
            //bool? x7 = GetPlcXOctal(7);
            //if (x7 == false)
            //{
            //    x7LowCount++;
            //}
            //else
            //{
            //    x7LowCount = 0;
            //}
            //if (x7LowCount >= DebounceCount)
            //{
            //    x7LowCount = 0;
            //    foreach (var n in _axisNames)
            //        if (Axes.TryGetValue(n, out var ax)) try { ax.MotStop(isImmediate: true); } catch { }
            //    progress?.Report("[Home] ⚠ 光閘觸發！等待 X10=1 繼續歸原點...");
            //    _logger.Warn("[Home] X7 confirmed LOW (debounced), axes stopped, waiting for X10=1 + X7=1");
            //    while (true)
            //    {
            //        ct.ThrowIfCancellationRequested();
            //        bool x10 = GetPlcXOctal(10) ?? false;
            //        bool x7Clear = GetPlcXOctal(7) ?? false;
            //        if (x10 && x7Clear) { _logger.Info("[Home] X10=1, X7=1 → resume homing"); break; }
            //        await Task.Delay(100, ct);
            //    }
            //    progress?.Report("[Home] 光閘解除，重新歸原點...");
            //    _logger.Info("[Home] X10=1, re-issuing Home to all axes");
            //    foreach (var axisName in _axisNames)
            //    {
            //        if (!Axes.TryGetValue(axisName, out var axis)) continue;
            //        if (axis is cAxis_RS485 rs485Axis)
            //            rs485Axis.Controller.SetHomingSpeed(highSpeedRpm: 8000, lowSpeedRpm: 800);
            //        axis.Home();
            //    }
            //    sw.Restart();
            //    continue;
            //}

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
            // 1. 列舉所有 GigE 讀碼器裝置
            var enumerator = new MvDeviceEnumerator();
            var devices = enumerator.EnumerateDevices();

            if (devices.Count == 0)
                return DeviceInitResult.Fail(name, "No MvCodeReader device found");

            // 2. 建立 SDK 控制物件與解析器
            _barcodeDevice = new MvCodeReaderDevice();
            _barcodeParser = new MvBarcodeResultParser();

            // 3. 用第一台裝置建立 Handle
            int ret = _barcodeDevice.CreateHandle(devices[0].RawDeviceInfo!);
            if (ret != 0)
                return DeviceInitResult.Fail(name, $"CreateHandle failed: 0x{ret:X}");

            // 4. 開啟裝置
            ret = _barcodeDevice.OpenDevice();
            if (ret != 0)
            {
                _barcodeDevice.DestroyHandle();
                return DeviceInitResult.Fail(name, $"OpenDevice failed: 0x{ret:X}");
            }

            // 5. 設定為軟體觸發模式（由程式決定何時讀碼，而非連續觸發）
            _barcodeDevice.SetEnumValue("TriggerMode",
                (uint)MvCodeReader.MV_CODEREADER_TRIGGER_MODE.MV_CODEREADER_TRIGGER_MODE_ON);
            _barcodeDevice.SetEnumValue("TriggerSource",
                (uint)MvCodeReader.MV_CODEREADER_TRIGGER_SOURCE.MV_CODEREADER_TRIGGER_SOURCE_SOFTWARE);

            // 6. 開始取像（進入等待觸發狀態）
            ret = _barcodeDevice.StartGrabbing();
            if (ret != 0)
                return DeviceInitResult.Fail(name, $"StartGrabbing failed: 0x{ret:X}");

            _logger.Info($"{name}: OK ({devices[0].DisplayName})");
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
    /// </summary>
    private DeviceInitResult InitPlc()
    {
        const string name = "PLC (FX)";
        try
        {
            // 建立串口 Transport → FX 協定通訊器
            var transport = new SerialBytesCommunicator(
                _plcComPort, _plcBaudRate, 7, Parity.Even, StopBits.One);
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
        CancellationToken ct = default)
    {
        // 清除上一次因中斷而殘留的暫停狀態
        _dryRunPaused = false;

        _logger.Info("===== Dry Run Start =====");

        if (SimulationMode)
        {
            await DryRunSimAsync(progress, ct);
            return;
        }
        Axes["AxisX"].SetMaxVel(8000);
        Axes["AxisY"].SetMaxVel(8000);
        Axes["AxisZL"].SetMaxVel(4000);
        Axes["AxisZR"].SetMaxVel(4000);

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

        // ── 背景光閘監控（暫時停用）──
        using var x7MonitorCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var x7MonitorTask = Task.CompletedTask;
        //var x7MonitorTask = Task.Run(async () =>
        //{
        //    const int DebounceCount = 3; // 連續幾次 LOW 才視為真實觸發
        //    int lowCount = 0;
        //    while (!x7MonitorCts.Token.IsCancellationRequested)
        //    {
        //        try
        //        {
        //            bool? x7 = GetPlcXOctal(7);
        //            if (x7 == false) // null（斷線）不計入，只有確定 LOW 才累計
        //            {
        //                lowCount++;
        //                if (lowCount >= DebounceCount)
        //                {
        //                    if (!_dryRunPaused)
        //                        _logger.Warn($"[DryRun] X7 debounce confirmed LOW ({lowCount}×50ms), pausing");
        //                    PauseDryRun();
        //                }
        //            }
        //            else
        //            {
        //                if (lowCount > 0 && lowCount < DebounceCount)
        //                    _logger.Debug($"[DryRun] X7 glitch suppressed ({lowCount}×50ms)");
        //                lowCount = 0; // HIGH 或斷線都重設計數
        //            }
        //            await Task.Delay(50, x7MonitorCts.Token);
        //        }
        //        catch (OperationCanceledException) { break; }
        //        catch { break; }
        //    }
        //});
        _logger.Info("[DryRun] X7 monitor disabled");

        try
        {
            // =====================================================
            //  STEP 0 : 前置確認
            // =====================================================

            // STEP 0-0 ① 確保光源已連線
            // if (_lightHB == null || !_lightHB.IsOpen)
            // {
            //     progress?.Report("[STEP 0-0] 初始化光源 (HighBright)...");
            //     var lr = InitLightHB();
            //     if (!lr.Success)
            //         throw new InvalidOperationException($"光源初始化失敗: {lr.Message}");
            //     _logger.Info("[STEP 0-0] 光源初始化 ✓");
            // }

            // STEP 0-0 ② 強制關閉光源（防呆：上次流程中斷時可能殘留亮燈狀態）
            // progress?.Report("[STEP 0-0] 光源全關（防呆）...");
            // bool off1 = LightSetWithRetry(1, 0, "STEP0-0", "OFF");
            // bool off2 = LightSetWithRetry(2, 0, "STEP0-0", "OFF");
            // if (!off1 || !off2)
            //     _logger.Warn($"[STEP 0-0] 光源強制關閉失敗  CH1={off1} CH2={off2}，請確認光源控制器連線");
            // else
            //     _logger.Info("[STEP 0-0] 光源全關 ✓");
            // progress?.Report("[STEP 0-0] 光源已關 ✓");

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
                        throw new InvalidOperationException($"{axisName} 警報無法清除，請檢查驅動器");
                }

                if (!axis.GetSVON())
                {
                    progress?.Report($"[STEP 0-1] {axisName} 激磁...");
                    axis.SetSVON(true);
                    bool ready = await WaitUntilAsync(
                        () => axis.GetRDY() && !axis.GetAlarm(),
                        _deviceConnectTimeout, ct);
                    if (!ready)
                        throw new InvalidOperationException(
                            $"{axisName} 激磁失敗 (RDY={axis.GetRDY()}, Alarm={axis.GetAlarm()})");
                }

                progress?.Report($"[STEP 0-1] {axisName} 就緒 ✓");
            }

            // TODO: 相機功能暫時停用
            if (_cameraRight == null || _cameraLeft == null)
            {
                progress?.Report("初始化相機...");
                var cr = await InitCamerasAsync(ct);
                if (!cr.Success)
                    throw new InvalidOperationException($"相機初始化失敗: {cr.Message}");
                progress?.Report("相機 ✓");
            }

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
            for (int i = 0; i < count; i++)
            {
                ct.ThrowIfCancellationRequested();
                await CheckPauseAsync(progress, ct);
                currentSlot++;

                var pos = SlotPositionTable.Get(target, i);
                string slotLabel = $"{target} Slot#{i + 1}";

                // STEP 1: Y 軸移動到目標 Slot 位置
                double clampedY = Math.Clamp(pos.Y, 0.0, YAxisMaxMm);
                progress?.Report($"({currentSlot}/{totalSlots}) {slotLabel}: Y→{clampedY:F1} mm...");
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
                progress?.Report($"({currentSlot}/{totalSlots}) {slotLabel}: Y={realY:F2} ✓");
                _logger.Info($"[DryRun] {slotLabel} Y={realY:F2}");

                // ── 光閘檢查：Y 到位後，若 X7 觸發則暫停 ──
                await CheckPauseAsync(progress, ct);

                // STEP 2: ZL + ZR 同時下降至檢測高度
                progress?.Report($"({currentSlot}/{totalSlots}) {slotLabel}: ZL↓ {_config.CameraHeightZL}mm / ZR↓ {_config.CameraHeightZR}mm...");
                Axes["AxisZL"].MotMoveAbs(_config.CameraHeightZL);
                Axes["AxisZR"].MotMoveAbs(_config.CameraHeightZR);

                bool zDown = await WaitUntilAsync(
                    () => Axes["AxisZL"].Wait() && Axes["AxisZR"].Wait(),
                    _config.MoveTimeout, ct);
                if (!zDown)
                    throw new TimeoutException($"{slotLabel} ZL/ZR 下降逾時");
                DryRunCheckAxisState("AxisZL", slotLabel);
                DryRunCheckAxisState("AxisZR", slotLabel);

                double realZL = Axes["AxisZL"].GetRealPosition();
                double realZR = Axes["AxisZR"].GetRealPosition();
                progress?.Report($"({currentSlot}/{totalSlots}) {slotLabel}: ZL={realZL:F2} ZR={realZR:F2} ✓");
                _logger.Info($"[DryRun] {slotLabel} ZL={realZL:F2} ZR={realZR:F2}");

                // ── 光閘檢查：Z 到位後，若 X7 觸發則暫停 ──
                await CheckPauseAsync(progress, ct);

                // STEP 3: ZL/ZR 到位後，在原本 2 秒停留期間執行「開光 → 取像 → 關光」
                await DryRunCaptureAsync(slotLabel, currentSlot, totalSlots, progress, ct);

                // ── 光閘檢查：取像完成後，若 X7 觸發則暫停（相機已完成，不繼續下一張）──
                await CheckPauseAsync(progress, ct);

                // STEP 4: ZL + ZR 上升恢復安全高度
                progress?.Report($"({currentSlot}/{totalSlots}) {slotLabel}: ZL+ZR↑ 恢復安全高度...");
                Axes["AxisZL"].MotMoveAbs(_config.ZSafeHeight);
                Axes["AxisZR"].MotMoveAbs(_config.ZSafeHeight);

                bool zUp = await WaitUntilAsync(
                    () => Axes["AxisZL"].Wait() && Axes["AxisZR"].Wait(),
                    _config.MoveTimeout, ct);
                if (!zUp)
                    throw new TimeoutException($"{slotLabel} ZL/ZR 上升逾時");
                DryRunCheckAxisState("AxisZL", slotLabel);
                DryRunCheckAxisState("AxisZR", slotLabel);

                progress?.Report($"({currentSlot}/{totalSlots}) {slotLabel}: ZL+ZR 已恢復 ✓");
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

        bool homeDone = await WaitUntilAsync(
            () => dryRunHomeAxes.All(n => Axes.TryGetValue(n, out var ax) && ax.Wait()),
            _axisHomeTimeout, ct);
        if (!homeDone)
            _logger.Warn("[STEP 2] ZL/ZR 回原點逾時，請確認軸狀態");
        else
            progress?.Report("[STEP 2] ZL+ZR 已回原點 ✓");

        // =====================================================
        //  STEP 3 : Y 軸移動至待機位置（496 mm）
        // =====================================================
        progress?.Report("[STEP 3] Y 軸移動至待機位置 496 mm...");
        _logger.Info("[DryRun] STEP 3 Y → 496 mm (standby)");

        // 若 X7 觸發暫停，等待恢復後再發出移動指令
        await CheckPauseAsync(progress, ct);

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            Axes["AxisY"].MotMoveAbs(496.0);
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
                progress?.Report($"[STEP 3] Y 已到達 496 mm (實際={Axes["AxisY"].GetRealPosition():F2}) ✓");
            break;
        }

        progress?.Report($"空跑測試完成 ✓  共跑完 {totalSlots} 個 Slot");
        _logger.Info("===== Dry Run Done =====");
        } // end try
        finally
        {
            // 無論正常結束、中斷、事件發生，光源一定強制關閉
            // if (_lightHB != null && _lightHB.IsOpen)
            // {
            //     try
            //     {
            //         _lightHB.SetValue(1, 0);
            //         _lightHB.SetValue(2, 0);
            //         _logger.Info("[DryRun] finally: 光源全關 ✓");
            //     }
            //     catch (Exception ex)
            //     {
            //         _logger.Warn($"[DryRun] finally: 光源關閉失敗 {ex.Message}");
            //     }
            // }

            // 停止 X7 背景監控
            x7MonitorCts.Cancel();
            try { await x7MonitorTask; } catch { }
            _logger.Info("[DryRun] X7 monitor stop");

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
        IProgress<string>? progress,
        CancellationToken ct)
    {
        const string SaveRoot     = @"D:\CameraLightTest";
        const int    LightChLeft  = 1;    // 左相機光源通道
        const int    LightChRight = 2;    // 右相機光源通道
        const int    LightPct     = 100;  // 亮度 100%（可依需求調整）
        const int    StabilizeMs  = 100;  // 光源穩定等待（ms）
        // Right 相機 ExposureTime=100000μs(100ms)，需等曝光 + Sapera DMA 傳輸完成
        // 取最長曝光 100ms + 100ms 傳輸緩衝 = 200ms → 再加 100ms 安全餘量 = 300ms
        const int    GrabWaitMs   = 300;

        // TODO: 拍照存圖暫時停用
        // STEP 1: 先停止相機，清除 IsGrabing 狀態
        _cameraRight?.Stop();
        _cameraLeft?.Stop();

        // STEP 2: CH1（左）+ CH2（右）光源同時 ON
        progress?.Report($"({currentSlot}/{totalSlots}) {slotLabel}: CH{LightChLeft}+CH{LightChRight} 光源 ON...");
        bool ch1On = LightSetWithRetry(LightChLeft, LightPct, slotLabel, "ON");
        bool ch2On = LightSetWithRetry(LightChRight, LightPct, slotLabel, "ON");
        if (!ch1On || !ch2On)
            _logger.Warn($"[DryRun] {slotLabel} 光源 ON 失敗  CH{LightChLeft}={ch1On} CH{LightChRight}={ch2On}");
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

        // STEP 6: CH1（左）+ CH2（右）光源 OFF
        bool ch1Off = LightSetWithRetry(LightChLeft, 0, slotLabel, "OFF");
        bool ch2Off = LightSetWithRetry(LightChRight, 0, slotLabel, "OFF");
        if (!ch1Off || !ch2Off)
            _logger.Warn($"[DryRun] {slotLabel} 光源 OFF 失敗  CH{LightChLeft}={ch1Off} CH{LightChRight}={ch2Off}");

        // STEP 7: 讀取 Buffer
        var bufRight = _cameraRight?.GetBufAddress();
        var bufLeft = _cameraLeft?.GetBufAddress();

        // STEP 8: 存 BMP 至 D:\CameraLightTest\yyyyMMdd\
        string saveLabel = slotLabel.Replace(" ", "_");
        string dir = Path.Combine(SaveRoot, DateTime.Now.ToString("yyyyMMdd"));
        Directory.CreateDirectory(dir);
        string ts = DateTime.Now.ToString("HHmmss_fff");

        bool anySaved = false;

        if (_cameraRight != null && bufRight != null && bufRight.Length > 0 && bufRight[0] != IntPtr.Zero)
        {
            string path = Path.Combine(dir, $"{saveLabel}_R_{ts}.bmp");
            DryRunSaveBmp(_cameraRight, bufRight[0], path);
            _logger.Info($"[DryRun] {slotLabel} 已存 R: {path}");
            anySaved = true;
        }
        if (_cameraLeft != null && bufLeft != null && bufLeft.Length > 0 && bufLeft[0] != IntPtr.Zero)
        {
            string path = Path.Combine(dir, $"{saveLabel}_L_{ts}.bmp");
            DryRunSaveBmp(_cameraLeft, bufLeft[0], path);
            _logger.Info($"[DryRun] {slotLabel} 已存 L: {path}");
            anySaved = true;
        }

        if (!anySaved)
        {
            _logger.Warn($"[DryRun] {slotLabel} 取像失敗（兩鏡頭皆無影像）");
            progress?.Report($"({currentSlot}/{totalSlots}) {slotLabel}: ⚠ 取像失敗");
        }
        else
        {
            progress?.Report($"({currentSlot}/{totalSlots}) {slotLabel}: 取像完成 ✓ → {dir}");
        }

        // STEP 9: 補足剩餘停留時間（確保整體 ≈ 2 秒）
        int remaining = 2000 - StabilizeMs - GrabWaitMs;
        if (remaining > 0)
            await Task.Delay(remaining, ct);
    }

    /// <summary>DryRun 專用：從 ICamera Buffer 存成 BMP 檔（與 CameraLightTest 相同做法）</summary>
    private void DryRunSaveBmp(ICamera camera, IntPtr bufPtr, string filePath)
    {
        try
        {
            int w      = camera.FrameWidth;
            int h      = camera.BufHeight;
            int stride = w * camera.PixelBytes;
            var fmt    = camera.PixelBytes == 3
                ? System.Windows.Media.PixelFormats.Rgb24
                : System.Windows.Media.PixelFormats.Gray8;

            var bmp = System.Windows.Media.Imaging.BitmapSource.Create(
                w, h, 96, 96, fmt, null, bufPtr, h * stride, stride);
            bmp.Freeze();

            using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write);
            var encoder = new System.Windows.Media.Imaging.BmpBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
            encoder.Save(fs);
        }
        catch (Exception ex)
        {
            _logger.Warn($"[DryRun] SaveBmp 失敗 ({filePath}): {ex.Message}");
        }
    }

    /// <summary>空跑：檢查軸狀態（警報/極限），有問題就拋例外或記 log</summary>
    private void DryRunCheckAxisState(string axisName, string context)
    {
        var axis = Axes[axisName];
        if (axis.GetAlarm())
            throw new InvalidOperationException($"{axisName} 於 {context} 發生警報");
        if (axis.GetPLimit())
            _logger.Warn($"[DryRun] {axisName} 正極限觸發 ({context})");
        if (axis.GetNLimit())
            _logger.Warn($"[DryRun] {axisName} 負極限觸發 ({context})");
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

    /// <summary>模擬模式的空跑：用 delay 模擬 ZL/ZR 升降 + Y 移動</summary>
    private async Task DryRunSimAsync(IProgress<string>? progress, CancellationToken ct)
    {
        var rows = new (string Label, int Count)[]
        {
            ("AreaA_Row1", 13),
            ("AreaA_Row2", 12),
        };

        int totalSlots = rows.Sum(r => r.Count);
        int current = 0;

        foreach (var (label, count) in rows)
        {
            for (int i = 0; i < count; i++)
            {
                ct.ThrowIfCancellationRequested();
                current++;
                string slot = $"{label} Slot#{i + 1}";
                progress?.Report($"[Sim] ({current}/{totalSlots}) {slot}: ZL+ZR↑ 安全高度...");
                await Task.Delay(150, ct);
                progress?.Report($"[Sim] ({current}/{totalSlots}) {slot}: Y 移動中...");
                await Task.Delay(200, ct);
                progress?.Report($"[Sim] ({current}/{totalSlots}) {slot}: ZL+ZR↓ 檢測高度...");
                await Task.Delay(150, ct);
                progress?.Report($"[Sim] ({current}/{totalSlots}) {slot}: 到位 ✓");
                await Task.Delay(100, ct);
            }
        }

        progress?.Report($"[Sim] 空跑測試完成 ✓ 已跑完 {totalSlots} 個 Slot");
        _logger.Info("===== Dry Run (Sim) Done =====");
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
            } catch { }
            try { _plc?.Dispose(); } catch { }
            try { _light?.Dispose(); } catch { }
            try { _lightHB?.Dispose(); } catch { }

            foreach (var axis in Axes.Values)
                try { axis.MotStop(); axis.SetSVON(false); } catch { }
        }

        _logger.Info("MachineController Disposed");
    }
}
