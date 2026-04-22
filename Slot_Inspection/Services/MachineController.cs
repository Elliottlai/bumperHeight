using Machine.Core;
using Machine.Core.Interfaces;
using Slot_Inspection.Models;
using FoupInspecMachine.Manager;
using NLog;
using System.IO;

namespace Slot_Inspection.Services;

/// <summary>
/// 整機控制器 — 管理所有設備的初始化與釋放。
/// S00 初始化的實際執行者。
/// </summary>
public sealed class MachineController : IDisposable
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

    // ══════════════════════════════════════════════════════
    //  模擬模式開關：Debug 時自動模擬，Release 時使用真實設備
    // ══════════════════════════════════════════════════════
#if DEBUG
    private const bool SimulationMode = true;
#else
    private const bool SimulationMode = false;
#endif

    // ── 設備名稱（待確認：請依實際設定檔中的 Key 調整）──
    private readonly string[] _axisNames = ["AxisX", "AxisY", "AxisZ", "AxisR"];
    private readonly string[] _cameraNames = ["Camera1", "Camera2"];

    // ── 通訊參數（待確認：請依實際接線調整）──
    private readonly string _lightComPort = "COM7";
    private readonly string _barcodeIp = "192.168.1.10";
    private readonly int _barcodePort = 8000;
    private readonly string _moxaIp = "192.168.127.254";
    private readonly int _moxaPort = 4001;

    // ── 超時設定 ──
    private readonly TimeSpan _axisHomeTimeout = TimeSpan.FromSeconds(60);
    private readonly TimeSpan _deviceConnectTimeout = TimeSpan.FromSeconds(10);

    // ── 設備實例（Init 後供後續步驟使用）──
    public Dictionary<string, IAxis> Axes => cMachineManager.Axises;
    public Dictionary<string, ICamera> Cameras => cMachineManager.Cameras;

    private FoupInspecMachine.Models.OPT_Controller? _light;
    private CameraManager? _cameraManager;
    private ModbusClientRtuOverTcp? _modbusClient;
    private bool _disposed;

    // ── 檢測參數（集中管理，日後可做成 Setting 頁面）──
    private readonly InspectionConfig _config = new();

    // ── 相機 Key ?? TODO：確認實際使用哪顆相機 ──
    private static readonly NamedKey _cameraKey = NamedKeyCamera.AreaCameraTop;

    // ═══════════════════════════════════════
    //  S00 主進入點
    // ═══════════════════════════════════════

    /// <summary>
    /// 初始化所有設備，回傳每個設備的成功/失敗結果。
    /// 順序有意義：設定檔 → 通訊底層 → 光源 → 軸 → 相機 → 讀碼器。
    /// </summary>
    public async Task<MachineInitResult> InitializeAllAsync(
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var result = new MachineInitResult();

        _logger.Info($"===== S00 初始化開始 (模擬模式: {SimulationMode}) =====");

        // ── 模擬模式：跳過所有硬體 ──
        if (SimulationMode)
         {
            string[] steps =
            [
                "設定檔載入", "IO 模組", "OPT 光控箱",
                "四軸伺服", "四軸歸零", "FLIR 相機", "海康讀碼器"
            ];

            foreach (var step in steps)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report($"[模擬] {step}...");
                await Task.Delay(300, ct);
                result.Add(DeviceInitResult.Ok($"{step} (模擬)"));
                _logger.Info($"{step} (模擬): OK");
            }

            _logger.Info("===== S00 模擬初始化完成 =====");
            return result;
        }

        // ── 真實模式 ──
        progress?.Report("正在載入設備設定...");
        result.Add(InitMachineCore());
        if (!result.AllPassed) return result;

        progress?.Report("正在初始化 IO 模組...");
        result.Add(InitIO());

        progress?.Report("正在初始化光源...");
        result.Add(InitLight());

        progress?.Report("正在啟用馬達伺服...");
        result.Add(await InitAxesAsync(ct));
        if (!result.AllPassed) return result;

        progress?.Report("正在歸零...");
        result.Add(await HomeAllAxesAsync(progress, ct));

        progress?.Report("正在初始化相機...");
        result.Add(InitCameras());

        progress?.Report("正在連線讀碼器...");
        result.Add(InitBarcodeReader());

        _logger.Info($"===== S00 初始化結束 =====");
        _logger.Info(result.GetSummary());

        return result;
    }

    // ═══════════════════════════════════════
    //  S02：等待放料就位
    // ═══════════════════════════════════════

    // ── 放料感測器參數（待確認：依實際接線調整）──
    private const byte MaterialSensorSlaveId = 1;   // Modbus 站號
    private const ushort MaterialSensorAddress = 0;  // DI 地址
    private readonly TimeSpan _materialTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// S02：等待感測器確認料已放到位。
    /// 模擬模式下延遲 1 秒自動通過。
    /// </summary>
    public async Task<bool> WaitForMaterialAsync(
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        _logger.Info("===== S02 等待放料 =====");

        if (SimulationMode)
        {
            progress?.Report("[模擬] 等待放料...");
            await Task.Delay(1000, ct); // 模擬操作員放料的時間
            _logger.Info("S02 (模擬): 放料就位");
            return true;
        }

        // ── 真實模式：輪詢 DI 感測器 ──
        progress?.Report("請放料...（等待感測器觸發）");

        bool placed = await WaitUntilAsync(
            condition: () => ReadMaterialSensor(),
            timeout: _materialTimeout,
            ct: ct);

        if (placed)
        {
            _logger.Info("S02: 放料就位（感測器觸發）");
        }
        else
        {
            _logger.Warn($"S02: 放料超時（{_materialTimeout.TotalSeconds}s）");
        }

        return placed;
    }

    /// <summary>
    /// 讀取放料感測器的 DI 狀態（正邏輯：有料 = true）。
    /// </summary>
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
            _logger.Warn(ex, "S02: 讀取感測器失敗");
            return false;
        }
    }

    // ═══════════════════════════════════════
    //  S03：檢測迴圈
    // ═══════════════════════════════════════

    /// <summary>
    /// 對全部 Slot 依序執行量測。
    /// 每個 Slot 完成後透過 progress 回報，ViewModel 再更新 UI。
    /// </summary>
    public async Task RunInspectionAsync(
        string barcode,
        IProgress<SlotInspectionProgress>? progress = null,
        CancellationToken ct = default)
    {
        _logger.Info($"===== S03 檢測開始 | 條碼: {barcode} =====");

        var zones = new (SlotInspectionProgress.TargetCollection Target, int Count)[]
        {
            (SlotInspectionProgress.TargetCollection.AreaA_Row1, 13),
            (SlotInspectionProgress.TargetCollection.AreaA_Row2, 12),
            (SlotInspectionProgress.TargetCollection.AreaB_Row1, 13),
            (SlotInspectionProgress.TargetCollection.AreaB_Row2, 12),
        };

        foreach (var (target, count) in zones)
        {
            for (int i = 0; i < count; i++)
            {
                ct.ThrowIfCancellationRequested();

                var (value, isNg, imagePath) = await InspectOneSlotAsync(barcode, target, i, ct);

                // S04：每個 Slot 完成立刻寫結果
                string slotName = $"{target}_Slot{i + 1}";
                InspectionResultWriter.WriteSlotResult(barcode, slotName, value, isNg, imagePath);

                progress?.Report(new SlotInspectionProgress
                {
                    Target     = target,
                    SlotIndex  = i,
                    Value      = value,
                    IsNg       = isNg,
                    StatusText = $"[{target}] Slot {i + 1}/{count} — {(isNg ? "NG" : "OK")}"
                });

                _logger.Debug($"[S03] {target}[{i}] Value={value:F2} IsNg={isNg}");
            }
        }

        _logger.Info($"===== S03 檢測結束 | 條碼: {barcode} =====");
    }

    /// <summary>
    /// 單一 Slot 完整量測流程（7 步驟）：
    ///   移軸 → 開光 → 拍照 → 存圖 → 關光 → 影像量測 → NG 判定
    /// 模擬模式：跳過所有硬體，產生假資料。
    /// </summary>
    private async Task<(double value, bool isNg, string imagePath)> InspectOneSlotAsync(
        string barcode,
        SlotInspectionProgress.TargetCollection target,
        int slotIndex,
        CancellationToken ct)
    {
        // ══════════════════════════════════
        //  模擬模式：完全繞過硬體
        // ══════════════════════════════════
        if (SimulationMode)
        {
            await Task.Delay(150, ct);
            var rng = new Random();
            double simValue = Math.Round(0.45 + rng.NextDouble() * 0.1, 2);
            bool simNg = rng.NextDouble() < 0.05;
            return (simValue, simNg, "");
        }

        // ══════════════════════════════════
        //  真實模式：7 步驟
        // ══════════════════════════════════

        string slotName = $"{target}_Slot{slotIndex + 1}";
        _logger.Debug($"[S03] 開始量測 {slotName}");

        // ── STEP 1：移軸到 Slot 座標 ──
        // ?? SlotPositionTable 目前座標全為 0（Stub）
        // ?? TODO：在真機教點後填入真實座標，這裡不用改
        var pos = SlotPositionTable.Get(target, slotIndex);
        Axes["AxisX"].MotMoveAbs(pos.X);
        Axes["AxisY"].MotMoveAbs(pos.Y);
        Axes["AxisZ"].MotMoveAbs(pos.Z);

        bool arrived = await WaitUntilAsync(
            () => Axes["AxisX"].Wait() && Axes["AxisY"].Wait() && Axes["AxisZ"].Wait(),
            _config.MoveTimeout, ct);

        if (!arrived)
            _logger.Warn($"[S03] {slotName} 移軸超時（座標尚未教點，先繼續）");

        // ── STEP 2：開光源 ──
        // ? OPT_Controller.SetValue
        _light!.SetValue(_config.LightChannel, _config.LightIntensity);
        await Task.Delay(_config.LightStabilizeMs, ct);

        // ── STEP 3：相機拍照 ──
        // ? CameraManager.CameraStart + GetCameraImage
        _cameraManager!.CameraStart(_cameraKey);
        await Task.Delay(_config.CaptureWaitMs, ct);
        var image = _cameraManager.GetCameraImage(_cameraKey);

        if (image == null)
        {
            _logger.Warn($"[S03] {slotName} 取像失敗，視為 NG");
            _light.SetValue(_config.LightChannel, 0);
            return (0, true, "");
        }

        // ── STEP 4：存圖（可選）──
        // ? CameraManager.SaveCameraImage
        string imagePath = "";
        if (_config.SaveImages)
        {
            string dir = Path.Combine(
                _config.ImageSavePath,
                DateTime.Now.ToString("yyyyMMdd"),
                barcode);
            Directory.CreateDirectory(dir);
            imagePath = Path.Combine(dir, $"{slotName}.tif");
            _cameraManager.SaveCameraImage(_cameraKey, imagePath, barcode, slotName);
        }

        // ── STEP 5：關光源 ──
        _light.SetValue(_config.LightChannel, 0);

        // ── STEP 6：影像量測 ──
        // ?? ImageMeasurer.Measure 目前是 Stub，固定回傳 0.50
        // ?? TODO：實作 Halcon 量測演算法後，這裡自動生效
        double measuredValue = ImageMeasurer.Measure(image, slotName);

        // ── STEP 7：NG 判定 ──
        bool isNg = measuredValue < _config.NgThresholdLow
                 || measuredValue > _config.NgThresholdHigh;

        _logger.Debug($"[S03] {slotName} 完成 Value={measuredValue:F4} isNg={isNg}");
        return (measuredValue, isNg, imagePath);
    }

    // ═══════════════════════════════════════
    //  各設備初始化
    // ═══════════════════════════════════════

    private DeviceInitResult InitMachineCore()
    {
        const string name = "設定檔載入";
        try
        {
            cMachineManager.Init();
            _logger.Info($"{name}: OK");
            return DeviceInitResult.Ok(name);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, $"{name}: FAIL");
            return DeviceInitResult.Fail(name, $"載入失敗: {ex.Message}", ex);
        }
    }

    private DeviceInitResult InitIO()
    {
        const string name = "IO 模組";
        try
        {
            var config = new ModbusConnectConifgTcp
            {
                IpAddress = _moxaIp,
                Port = _moxaPort
            };

            _modbusClient = new ModbusClientRtuOverTcp();
            bool connected = _modbusClient.Connect(config);

            if (!connected)
            {
                _logger.Warn($"{name}: Modbus 連線失敗 ({_moxaIp}:{_moxaPort})");
                return DeviceInitResult.Fail(name, $"Modbus 連線失敗 ({_moxaIp}:{_moxaPort})");
            }

            // 測試讀取一次 DI（待確認：站號、地址）
            byte[] testRead = _modbusClient.ReadDiscreteInputs(1, 0, 1);
            if (testRead == null || testRead.Length == 0)
            {
                _logger.Warn($"{name}: 測試讀取無回應");
                return DeviceInitResult.Fail(name, "測試讀取無回應");
            }

            _logger.Info($"{name}: OK");
            return DeviceInitResult.Ok(name);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, $"{name}: FAIL");
            return DeviceInitResult.Fail(name, ex.Message, ex);
        }
    }

    private DeviceInitResult InitLight()
    {
        const string name = "OPT 光控箱";
        try
        {
            _light = new FoupInspecMachine.Models.OPT_Controller(_lightComPort);
            _light.Open();

            if (!_light.IsOpen)
            {
                _logger.Warn($"{name}: 無法開啟 {_lightComPort}");
                return DeviceInitResult.Fail(name, $"無法開啟 {_lightComPort}");
            }

            // 測試通訊：讀 channel 1 亮度
            _ = _light.GetValue(1);

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
        const string name = "四軸伺服";
        try
        {
            foreach (var axisName in _axisNames)
            {
                ct.ThrowIfCancellationRequested();

                if (!Axes.TryGetValue(axisName, out var axis))
                {
                    _logger.Warn($"{name}: 找不到軸 {axisName}");
                    return DeviceInitResult.Fail(name, $"找不到軸: {axisName}");
                }

                axis.ResetError();
                axis.SetSVON(true);

                bool ready = await WaitUntilAsync(
                    () => axis.GetRDY() && !axis.GetAlarm(),
                    _deviceConnectTimeout, ct);

                if (!ready)
                {
                    var msg = $"{axisName} 未就緒 (RDY={axis.GetRDY()}, Alarm={axis.GetAlarm()})";
                    _logger.Warn($"{name}: {msg}");
                    return DeviceInitResult.Fail(name, msg);
                }

                _logger.Debug($"{axisName} ServoOn OK");
            }

            _logger.Info($"{name}: OK");
            return DeviceInitResult.Ok(name);
        }
        catch (OperationCanceledException)
        {
            _logger.Warn($"{name}: 使用者取消");
            return DeviceInitResult.Fail(name, "使用者取消");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, $"{name}: FAIL");
            return DeviceInitResult.Fail(name, ex.Message, ex);
        }
    }

    private async Task<DeviceInitResult> HomeAllAxesAsync(
        IProgress<string>? progress, CancellationToken ct)
    {
        const string name = "四軸歸零";
        try
        {
            foreach (var axisName in _axisNames)
            {
                ct.ThrowIfCancellationRequested();

                var axis = Axes[axisName];
                progress?.Report($"正在歸零: {axisName}...");
                _logger.Debug($"開始歸零: {axisName}");

                axis.Home();

                bool done = await WaitUntilAsync(
                    () => axis.Wait(),
                    _axisHomeTimeout, ct);

                if (!done)
                {
                    var msg = $"{axisName} 歸零超時（{_axisHomeTimeout.TotalSeconds}s）";
                    _logger.Warn($"{name}: {msg}");
                    return DeviceInitResult.Fail(name, msg);
                }

                if (!axis.GetOrg())
                {
                    var msg = $"{axisName} 歸零完成但原點感測器未觸發";
                    _logger.Warn($"{name}: {msg}");
                    return DeviceInitResult.Fail(name, msg);
                }

                if (axis.GetAlarm())
                {
                    var msg = $"{axisName} 歸零後 Alarm";
                    _logger.Warn($"{name}: {msg}");
                    return DeviceInitResult.Fail(name, msg);
                }

                if (axis.GetPLimit() || axis.GetNLimit())
                {
                    var msg = $"{axisName} 歸零後觸發極限";
                    _logger.Warn($"{name}: {msg}");
                    return DeviceInitResult.Fail(name, msg);
                }

                _logger.Debug($"{axisName} 歸零完成，位置={axis.GetRealPosition()}");
            }

            _logger.Info($"{name}: OK");
            return DeviceInitResult.Ok(name);
        }
        catch (OperationCanceledException)
        {
            // 取消時緊急停止所有軸
            foreach (var axisName in _axisNames)
            {
                if (Axes.TryGetValue(axisName, out var ax))
                {
                    try { ax.MotStop(isImmediate: true); } catch { }
                }
            }

            _logger.Warn($"{name}: 使用者取消，已停止所有軸");
            return DeviceInitResult.Fail(name, "使用者取消，已停止所有軸");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, $"{name}: FAIL");
            return DeviceInitResult.Fail(name, ex.Message, ex);
        }
    }

    private DeviceInitResult InitCameras()
    {
        const string name = "FLIR 相機";
        try
        {
            _cameraManager = new CameraManager(_isSimulation: false);

            foreach (var camName in _cameraNames)
            {
                if (!Cameras.TryGetValue(camName, out var camera))
                {
                    _logger.Warn($"{name}: 找不到 {camName}");
                    return DeviceInitResult.Fail(name, $"找不到相機: {camName}");
                }

                bool ok = camera.Init();
                if (!ok)
                {
                    _logger.Warn($"{name}: {camName} Init() 失敗");
                    return DeviceInitResult.Fail(name, $"{camName} Init() 失敗");
                }

                _logger.Debug($"{camName} Init OK");
            }

            _logger.Info($"{name}: OK");
            return DeviceInitResult.Ok(name);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, $"{name}: FAIL");
            return DeviceInitResult.Fail(name, ex.Message, ex);
        }
    }

    private DeviceInitResult InitBarcodeReader()
    {
        const string name = "海康讀碼器";
        try
        {
            // 待確認：實際使用 MV SDK 或 TCP Socket
            using var socket = new System.Net.Sockets.TcpClient();
            var connectTask = socket.ConnectAsync(_barcodeIp, _barcodePort);

            if (!connectTask.Wait(_deviceConnectTimeout))
            {
                _logger.Warn($"{name}: 連線 {_barcodeIp}:{_barcodePort} 超時");
                return DeviceInitResult.Fail(name, $"連線 {_barcodeIp}:{_barcodePort} 超時");
            }

            if (!socket.Connected)
            {
                _logger.Warn($"{name}: 無法連線 {_barcodeIp}:{_barcodePort}");
                return DeviceInitResult.Fail(name, $"無法連線 {_barcodeIp}:{_barcodePort}");
            }

            _logger.Info($"{name}: OK ({_barcodeIp}:{_barcodePort})");
            return DeviceInitResult.Ok(name);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, $"{name}: FAIL");
            return DeviceInitResult.Fail(name, ex.Message, ex);
        }
    }

    // ═══════════════════════════════════════
    //  工具方法
    // ═══════════════════════════════════════

    /// <summary>
    /// 帶超時的條件等待，取代 while(true) 無限迴圈。
    /// </summary>
    public static async Task<bool> WaitUntilAsync(
        Func<bool> condition,
        TimeSpan timeout,
        CancellationToken ct = default,
        int pollIntervalMs = 100)
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
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return false;
        }
    }

    // ═══════════════════════════════════════
    //  Dispose
    // ═══════════════════════════════════════

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (!SimulationMode)
        {
            try { _light?.Dispose(); } catch { }
            try { _modbusClient?.Dispose(); } catch { }

            foreach (var axis in Axes.Values)
            {
                try { axis.MotStop(); axis.SetSVON(false); } catch { }
            }
        }

        _logger.Info("MachineController Disposed");
    }
}
