using Machine.Core;
using Machine.Core.Interfaces;
using Slot_Inspection.Models;
using FoupInspecMachine.Manager;
using NLog;
using System.IO;
using System.IO.Ports;
using System.Runtime.InteropServices;
using BarcodeReader.Interfaces;
using BarcodeReader.Services;
using MvCodeReaderSDKNet;
using PLC_IO.Interfaces;
using PLC_IO.Services;

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

    // X=讀碼軸, Y=載台軸, Z=相機高度軸（無 R 軸）
    private readonly string[] _axisNames = ["AxisX", "AxisY", "AxisZ"];
    private readonly string[] _cameraNames = ["Camera1", "Camera2"];

    private readonly string _lightComPort = "COM7";
    private readonly string _barcodeIp = "192.168.1.10";
    private readonly int _barcodePort = 8000;
    private readonly string _moxaIp = "192.168.127.254";
    private readonly int _moxaPort = 4001;

    // -- PLC (三菱 FX) 串口設定 --
    private readonly string _plcComPort = "COM3";   // TODO: 改為實際 PLC 串口
    private readonly int _plcBaudRate = 115200;       // TODO: 改為實際鮑率

    private readonly TimeSpan _axisHomeTimeout = TimeSpan.FromSeconds(60);
    private readonly TimeSpan _deviceConnectTimeout = TimeSpan.FromSeconds(10);

    public Dictionary<string, IAxis> Axes => cMachineManager.Axises;
    public Dictionary<string, ICamera> Cameras => cMachineManager.Cameras;

    private FoupInspecMachine.Models.OPT_Controller? _light;
    private CameraManager? _cameraManager;
    private ModbusClientRtuOverTcp? _modbusClient;
    private ICodeReaderDevice? _barcodeDevice;    // 海康讀碼器 SDK 控制物件
    private IBarcodeResultParser? _barcodeParser;  // 讀碼結果解析器
    private IPlcCommunicator? _plc;               // 三菱 FX PLC 通訊
    private bool _disposed;

    private readonly InspectionConfig _config = new();
    private readonly BumperAlgService _bumperAlg = new();

    // -- Sim image loader (lazy init) --
    private SimImageLoader? _simImageLoader;
    private SimImageLoader GetSimImageLoader()
        => _simImageLoader ??= new SimImageLoader(_config.SimImageFolderPath);

    // -- Two cameras: Left & Right side of bumper --
    private static readonly NamedKey _cameraKeyLeft  = NamedKeyCamera.AreaCameraTop;
    private static readonly NamedKey _cameraKeyRight = NamedKeyCamera.AreaCameraSide;

    // =========================================
    //  S01: Carrier detection + Barcode scan
    // =========================================

    /// <summary>載台在席感測器對應的 PLC X 點位</summary>
    private const int CarrierLeftSensorIndex = 12;   // X12 = 左側在席
    private const int CarrierRightSensorIndex = 13;  // X13 = 右側在席

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
        bool left  = _plc.GetX(CarrierLeftSensorIndex);   // X12
        bool right = _plc.GetX(CarrierRightSensorIndex);  // X13

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
                "Config", "IO Module", "OPT Light",
                "3-Axis Servo", "3-Axis Home",
                "Camera Left", "Camera Right",
                "Barcode Reader", "PLC (FX)"
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

        progress?.Report("Init IO module...");
        result.Add(InitIO());

        progress?.Report("Init light...");
        result.Add(InitLight());

        progress?.Report("Enabling servo...");
        result.Add(await InitAxesAsync(ct));
        if (!result.AllPassed) return result;

        progress?.Report("Homing...");
        result.Add(await HomeAllAxesAsync(progress, ct));

        progress?.Report("Init cameras...");
        result.Add(InitCameras());

        progress?.Report("Connecting barcode reader...");
        result.Add(InitBarcodeReader());

        progress?.Report("Connecting PLC...");
        result.Add(InitPlc());

        _logger.Info("===== S00 Init Done =====");
        _logger.Info(result.GetSummary());
        return result;
    }

    /// <summary>
    /// 軸控專用初始化（空跑測試使用）。
    /// 只初始化 Config + Axis，跳過相機、光源、讀?器、PLC。
    /// Debug 模式下空跑前呼叫此方法，不需完整 S00 初始化。
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

        // ? 載入設定檔（讀取 Axises.json）
        progress?.Report("Loading config...");
        result.Add(InitMachineCore());
        if (!result.AllPassed) return result;

        // ? 激磁所有軸
        progress?.Report("Enabling servo...");
        result.Add(await InitAxesAsync(ct));
        if (!result.AllPassed) return result;

        // ? 全軸歸原點
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
    //  S02: Wait for material (已棄用)
    //  載台在席偵測（S01a）即代表有料，不需獨立等料步驟
    // =========================================

    private const byte MaterialSensorSlaveId = 1;
    private const ushort MaterialSensorAddress = 0;
    private readonly TimeSpan _materialTimeout = TimeSpan.FromSeconds(60);

    [Obsolete("載台在席偵測（S01a）即代表有料，不需獨立等料步驟。")]
    public async Task<bool> WaitForMaterialAsync(
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        _logger.Info("===== S02 Wait Material =====");

        if (SimulationMode)
        {
            progress?.Report("[Sim] Waiting for material...");
            await Task.Delay(1000, ct);
            _logger.Info("S02 (Sim): Material placed");
            return true;
        }

        progress?.Report("Place material... (waiting for sensor)");
        bool placed = await WaitUntilAsync(
            condition: () => ReadMaterialSensor(),
            timeout: _materialTimeout, ct: ct);

        if (placed)
            _logger.Info("S02: Material placed (sensor triggered)");
        else
            _logger.Warn($"S02: Material timeout ({_materialTimeout.TotalSeconds}s)");

        return placed;
    }

    private bool ReadMaterialSensor()
    {
        try
        {
            byte[] result = _modbusClient!.ReadDiscreteInputs(
                MaterialSensorSlaveId, MaterialSensorAddress, 1);
            return result != null && result.Length > 0 && result[0] != 0;
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "S02: Sensor read failed");
            return false;
        }
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

        // STEP 1: Y 軸移動載台到 Slot 位置，Z 軸調整相機高度
        var pos = SlotPositionTable.Get(target, slotIndex);
        Axes["AxisY"].MotMoveAbs(pos.Y);
        Axes["AxisZ"].MotMoveAbs(_config.CameraHeightZ);

        bool arrived = await WaitUntilAsync(
            () => Axes["AxisY"].Wait() && Axes["AxisZ"].Wait(),
            _config.MoveTimeout, ct);

        if (!arrived)
            _logger.Warn($"[S03] {slotName} move timeout");

        // STEP 2: Both lights ON simultaneously
        _light!.SetValue(_config.LightChannelLeft,  _config.LightIntensityLeft);
        _light.SetValue(_config.LightChannelRight, _config.LightIntensityRight);
        await Task.Delay(_config.LightStabilizeMs, ct);

        // STEP 3: Both cameras trigger simultaneously
        _cameraManager!.CameraStart(_cameraKeyLeft);
        _cameraManager.CameraStart(_cameraKeyRight);
        await Task.Delay(_config.CaptureWaitMs, ct);

        // STEP 4: Get both images
        var imageLeft  = _cameraManager.GetCameraImage(_cameraKeyLeft);
        var imageRight = _cameraManager.GetCameraImage(_cameraKeyRight);

        // STEP 5: Both lights OFF
        _light.SetValue(_config.LightChannelLeft,  0);
        _light.SetValue(_config.LightChannelRight, 0);

        // Capture failure check
        if (imageLeft == null || imageRight == null)
        {
            string failSide = imageLeft == null && imageRight == null ? "BOTH"
                            : imageLeft == null ? "LEFT" : "RIGHT";
            _logger.Warn($"[S03] {slotName} {failSide} capture failed, treat as NG");
            return (0, true, "", null);
        }

        // STEP 6: Save images
        string imagePath = "";
        string pathL = "", pathR = "";
        if (_config.SaveImages)
        {
            string dir = Path.Combine(
                _config.ImageSavePath,
                DateTime.Now.ToString("yyyyMMdd"),
                barcode);
            Directory.CreateDirectory(dir);

            pathL = Path.Combine(dir, $"{slotName}_L.tif");
            pathR = Path.Combine(dir, $"{slotName}_R.tif");

            _cameraManager.SaveCameraImage(_cameraKeyLeft,  pathL, barcode, $"{slotName}_L");
            _cameraManager.SaveCameraImage(_cameraKeyRight, pathR, barcode, $"{slotName}_R");

            imagePath = pathL;
        }

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
        try { cMachineManager.Init(); _logger.Info($"{name}: OK"); return DeviceInitResult.Ok(name); }
        catch (Exception ex) { _logger.Error(ex, $"{name}: FAIL"); return DeviceInitResult.Fail(name, $"Load failed: {ex.Message}", ex); }
    }

    private DeviceInitResult InitIO()
    {
        const string name = "IO Module";
        try
        {
            _modbusClient = new ModbusClientRtuOverTcp();
            bool connected = _modbusClient.Connect(new ModbusConnectConifgTcp { IpAddress = _moxaIp, Port = _moxaPort });
            if (!connected) return DeviceInitResult.Fail(name, $"Modbus connect failed ({_moxaIp}:{_moxaPort})");
            byte[] testRead = _modbusClient.ReadDiscreteInputs(1, 0, 1);
            if (testRead == null || testRead.Length == 0) return DeviceInitResult.Fail(name, "Test read no response");
            _logger.Info($"{name}: OK"); return DeviceInitResult.Ok(name);
        }
        catch (Exception ex) { _logger.Error(ex, $"{name}: FAIL"); return DeviceInitResult.Fail(name, ex.Message, ex); }
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
            foreach (var axisName in _axisNames)
            {
                ct.ThrowIfCancellationRequested();
                var axis = Axes[axisName];
                progress?.Report($"Homing: {axisName}...");
                axis.Home();
                bool done = await WaitUntilAsync(() => axis.Wait(), _axisHomeTimeout, ct);
                if (!done) return DeviceInitResult.Fail(name, $"{axisName} home timeout ({_axisHomeTimeout.TotalSeconds}s)");
                if (!axis.GetOrg()) return DeviceInitResult.Fail(name, $"{axisName} home done but ORG not triggered");
                if (axis.GetAlarm()) return DeviceInitResult.Fail(name, $"{axisName} alarm after home");
                if (axis.GetPLimit() || axis.GetNLimit()) return DeviceInitResult.Fail(name, $"{axisName} limit triggered after home");
                _logger.Debug($"{axisName} home done, pos={axis.GetRealPosition()}");
            }
            _logger.Info($"{name}: OK"); return DeviceInitResult.Ok(name);
        }
        catch (OperationCanceledException)
        {
            foreach (var axisName in _axisNames)
                if (Axes.TryGetValue(axisName, out var ax)) try { ax.MotStop(isImmediate: true); } catch { }
            return DeviceInitResult.Fail(name, "Cancelled, all axes stopped");
        }
        catch (Exception ex) { _logger.Error(ex, $"{name}: FAIL"); return DeviceInitResult.Fail(name, ex.Message, ex); }
    }

    private DeviceInitResult InitCameras()
    {
        const string name = "FLIR Camera";
        try
        {
            _cameraManager = new CameraManager(_isSimulation: false);
            foreach (var camName in _cameraNames)
            {
                if (!Cameras.TryGetValue(camName, out var camera))
                    return DeviceInitResult.Fail(name, $"Camera not found: {camName}");
                if (!camera.Init()) return DeviceInitResult.Fail(name, $"{camName} Init() failed");
                _logger.Debug($"{camName} Init OK");
            }
            _logger.Info($"{name}: OK"); return DeviceInitResult.Ok(name);
        }
        catch (Exception ex) { _logger.Error(ex, $"{name}: FAIL"); return DeviceInitResult.Fail(name, ex.Message, ex); }
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
    //  Dry Run (空跑測試)
    // =========================================

    /// <summary>
    /// 空跑測試：模擬完整生產流程的軸控路徑，驗證每根軸能正確到達對應位置。
    /// 排除掃碼、相機取像、光源控制，僅測試軸的移動與到位。
    /// 路徑完全對應實際生產流程：
    ///   S01b → AxisX 移動到讀碼位置（左/右）
    ///   S01c → AxisX 回原點
    ///   S03  → 逐 Slot 移動 AxisY 到載台位置 + AxisZ 到相機高度
    ///   結束 → 全部回原點
    /// </summary>
    public async Task DryRunAsync(
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        _logger.Info("===== Dry Run Start =====");

        if (SimulationMode)
        {
            await DryRunSimAsync(progress, ct);
            return;
        }

        // ── 前置：確認所有軸已激磁且無警報 ──
        foreach (var axisName in _axisNames)
        {
            ct.ThrowIfCancellationRequested();
            if (!Axes.TryGetValue(axisName, out var axis))
                throw new InvalidOperationException($"{axisName} 不存在");

            if (axis.GetAlarm())
            {
                progress?.Report($"? {axisName} 有警報，嘗試清除...");
                axis.ResetError();
                await Task.Delay(500, ct);
                if (axis.GetAlarm())
                    throw new InvalidOperationException($"{axisName} 警報無法清除，請檢查驅動器");
            }

            if (!axis.GetSVON())
            {
                progress?.Report($"{axisName} 激磁...");
                axis.SetSVON(true);
                bool ready = await WaitUntilAsync(
                    () => axis.GetRDY() && !axis.GetAlarm(),
                    _deviceConnectTimeout, ct);
                if (!ready)
                    throw new InvalidOperationException(
                        $"{axisName} 激磁失敗 (RDY={axis.GetRDY()}, Alarm={axis.GetAlarm()})");
            }

            progress?.Report($"{axisName} 激磁完成 ?");
        }

        // ── S01b 模擬：AxisX 移動到讀碼位置 ──
        progress?.Report("[S01b] AxisX → 讀碼位置（左）...");
        await DryRunMoveAsync("AxisX", _config.BarcodePositionLeftX, progress, ct);

        progress?.Report("[S01b] AxisX → 讀碼位置（右）...");
        await DryRunMoveAsync("AxisX", _config.BarcodePositionRightX, progress, ct);

        // ── S01c 模擬：AxisX 回原點 ──
        progress?.Report("[S01c] AxisX 回原點...");
        Axes["AxisX"].Home();
        bool xHome = await WaitUntilAsync(() => Axes["AxisX"].Wait(), _axisHomeTimeout, ct);
        if (!xHome) throw new TimeoutException("AxisX 回原點逾時");
        progress?.Report($"[S01c] AxisX 回原點完成 (pos={Axes["AxisX"].GetRealPosition():F2}) ?");

        // ── S03 模擬：逐 Slot 移動 AxisY + AxisZ 到檢測位置 ──
        var rows = new (SlotInspectionProgress.TargetCollection Target, int Count)[]
        {
            (SlotInspectionProgress.TargetCollection.AreaA_Row1, 13),
            (SlotInspectionProgress.TargetCollection.AreaA_Row2, 12),
        };

        int totalSlots = rows.Sum(r => r.Count);
        int currentSlot = 0;

        foreach (var (target, count) in rows)
        {
            for (int i = 0; i < count; i++)
            {
                ct.ThrowIfCancellationRequested();
                currentSlot++;

                var pos = SlotPositionTable.Get(target, i);
                string slotLabel = $"{target} Slot#{i + 1}";

                progress?.Report($"[S03] ({currentSlot}/{totalSlots}) {slotLabel}: Y→{pos.Y:F1}, Z→{_config.CameraHeightZ:F1}...");

                // 同時移動 Y 和 Z（與實際生產流程一致）
                Axes["AxisY"].MotMoveAbs(pos.Y);
                Axes["AxisZ"].MotMoveAbs(_config.CameraHeightZ);

                bool arrived = await WaitUntilAsync(
                    () => Axes["AxisY"].Wait() && Axes["AxisZ"].Wait(),
                    _config.MoveTimeout, ct);

                if (!arrived)
                    throw new TimeoutException($"{slotLabel} 移動逾時 (Y 或 Z 未到位)");

                // 檢查警報與極限
                DryRunCheckAxisState("AxisY", slotLabel);
                DryRunCheckAxisState("AxisZ", slotLabel);

                double realY = Axes["AxisY"].GetRealPosition();
                double realZ = Axes["AxisZ"].GetRealPosition();
                progress?.Report($"[S03] ({currentSlot}/{totalSlots}) {slotLabel}: Y={realY:F2}, Z={realZ:F2} ?");
                _logger.Info($"[DryRun] {slotLabel} Y={realY:F2} Z={realZ:F2}");

                await Task.Delay(200, ct); // 短暫停留
            }
        }

        // ── 結束：所有軸回原點 ──
        progress?.Report("所有軸回原點...");
        foreach (var axisName in _axisNames)
        {
            ct.ThrowIfCancellationRequested();
            if (Axes.TryGetValue(axisName, out var axis))
                axis.Home();
        }

        bool allHome = await WaitUntilAsync(
            () => _axisNames.All(n => !Axes.TryGetValue(n, out var a) || a.Wait()),
            _axisHomeTimeout, ct);

        if (!allHome)
            _logger.Warn("[DryRun] Some axes did not reach home in time");

        progress?.Report($"空跑測試完成 ? 已跑完 {totalSlots} 個 Slot 位置，所有軸已回原點");
        _logger.Info("===== Dry Run Done =====");
    }

    /// <summary>空跑：移動單軸到指定位置並驗證到位</summary>
    private async Task DryRunMoveAsync(
        string axisName, double targetPos,
        IProgress<string>? progress, CancellationToken ct)
    {
        var axis = Axes[axisName];
        axis.MotMoveAbs(targetPos);

        bool arrived = await WaitUntilAsync(
            () => axis.Wait(), _config.MoveTimeout, ct);

        if (!arrived)
            throw new TimeoutException($"{axisName} 移動到 {targetPos:F1} mm 逾時");

        DryRunCheckAxisState(axisName, $"target={targetPos:F1}");

        double realPos = axis.GetRealPosition();
        progress?.Report($"{axisName} 到位 ({realPos:F2} mm) ?");
        _logger.Info($"[DryRun] {axisName} arrived at {realPos:F2} mm (target={targetPos:F1})");

        await Task.Delay(300, ct);
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

    /// <summary>模擬模式的空跑：走完整流程路徑但用 delay 模擬</summary>
    private async Task DryRunSimAsync(IProgress<string>? progress, CancellationToken ct)
    {
        // S01b: AxisX 讀碼位置
        progress?.Report("[Sim][S01b] AxisX → 讀碼位置（左）...");
        await Task.Delay(400, ct);
        progress?.Report($"[Sim][S01b] AxisX 到位 ({_config.BarcodePositionLeftX:F1} mm) ?");
        await Task.Delay(200, ct);

        progress?.Report("[Sim][S01b] AxisX → 讀碼位置（右）...");
        await Task.Delay(400, ct);
        progress?.Report($"[Sim][S01b] AxisX 到位 ({_config.BarcodePositionRightX:F1} mm) ?");
        await Task.Delay(200, ct);

        // S01c: AxisX 回原點
        progress?.Report("[Sim][S01c] AxisX 回原點...");
        await Task.Delay(400, ct);
        progress?.Report("[Sim][S01c] AxisX 回原點完成 ?");

        // S03: 逐 Slot
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
                progress?.Report($"[Sim][S03] ({current}/{totalSlots}) {slot}: Y+Z 移動中...");
                await Task.Delay(250, ct);
                progress?.Report($"[Sim][S03] ({current}/{totalSlots}) {slot}: 到位 ?");
                await Task.Delay(100, ct);
            }
        }

        // 回原點
        progress?.Report("[Sim] 所有軸回原點...");
        await Task.Delay(500, ct);
        progress?.Report($"[Sim] 空跑測試完成 ? 已跑完 {totalSlots} 個 Slot 位置");
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
            try { _plc?.Dispose(); } catch { }
            try { _light?.Dispose(); } catch { }
            try { _modbusClient?.Dispose(); } catch { }

            foreach (var axis in Axes.Values)
                try { axis.MotStop(); axis.SetSVON(false); } catch { }
        }

        _logger.Info("MachineController Disposed");
    }
}
