using Machine.Core;
using Machine.Core.Interfaces;
using Slot_Inspection.Models;
using FoupInspecMachine.Manager;
using NLog;
using System.IO;

namespace Slot_Inspection.Services;

/// <summary>
/// Machine controller - manages all device init and release.
/// S00 initialization executor.
/// </summary>
public sealed class MachineController : IDisposable
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

#if DEBUG
    private const bool SimulationMode = true;
#else
    private const bool SimulationMode = false;
#endif

    private readonly string[] _axisNames = ["AxisX", "AxisY", "AxisZ", "AxisR"];
    private readonly string[] _cameraNames = ["Camera1", "Camera2"];

    private readonly string _lightComPort = "COM7";
    private readonly string _barcodeIp = "192.168.1.10";
    private readonly int _barcodePort = 8000;
    private readonly string _moxaIp = "192.168.127.254";
    private readonly int _moxaPort = 4001;

    private readonly TimeSpan _axisHomeTimeout = TimeSpan.FromSeconds(60);
    private readonly TimeSpan _deviceConnectTimeout = TimeSpan.FromSeconds(10);

    public Dictionary<string, IAxis> Axes => cMachineManager.Axises;
    public Dictionary<string, ICamera> Cameras => cMachineManager.Cameras;

    private FoupInspecMachine.Models.OPT_Controller? _light;
    private CameraManager? _cameraManager;
    private ModbusClientRtuOverTcp? _modbusClient;
    private bool _disposed;

    private readonly InspectionConfig _config = new();

    // -- Sim image loader (lazy init) --
    private SimImageLoader? _simImageLoader;
    private SimImageLoader GetSimImageLoader()
        => _simImageLoader ??= new SimImageLoader(_config.SimImageFolderPath);

    private static readonly NamedKey _cameraKey = NamedKeyCamera.AreaCameraTop;

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
                "4-Axis Servo", "4-Axis Home", "FLIR Camera", "Barcode Reader"
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

        _logger.Info("===== S00 Init Done =====");
        _logger.Info(result.GetSummary());
        return result;
    }

    // =========================================
    //  S02: Wait for material
    // =========================================

    private const byte MaterialSensorSlaveId = 1;
    private const ushort MaterialSensorAddress = 0;
    private readonly TimeSpan _materialTimeout = TimeSpan.FromSeconds(60);

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

                var (value, isNg, imagePath, image) = await InspectOneSlotAsync(barcode, target, i, ct);

                string slotName = $"{target}_Slot{i + 1}";
                InspectionResultWriter.WriteSlotResult(barcode, slotName, value, isNg, imagePath);

                progress?.Report(new SlotInspectionProgress
                {
                    Target     = target,
                    SlotIndex  = i,
                    Value      = value,
                    IsNg       = isNg,
                    StatusText = $"[{target}] Slot {i + 1}/{count} - {(isNg ? "NG" : "OK")}",
                    Image      = image
                });

                _logger.Debug($"[S03] {target}[{i}] Value={value:F2} IsNg={isNg}");
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
            var rng = new Random();
            double simValue = Math.Round(0.45 + rng.NextDouble() * 0.1, 2);
            bool simNg = rng.NextDouble() < 0.05;
            string slotLabel = $"{target}_S{slotIndex + 1}";

            // Try loading from test image folder; fallback to generated color block
            var loader = GetSimImageLoader();
            System.Windows.Media.ImageSource? simImage = loader.HasImages
                ? (loader.Next() ?? SimImageGenerator.Generate(slotLabel, simValue, simNg))
                : SimImageGenerator.Generate(slotLabel, simValue, simNg);

            return (simValue, simNg, "", simImage);
        }

        // ==================================
        //  Real mode: 7 steps
        // ==================================

        string slotName = $"{target}_Slot{slotIndex + 1}";
        _logger.Debug($"[S03] Start measuring {slotName}");

        // STEP 1: Move axis to Slot position
        var pos = SlotPositionTable.Get(target, slotIndex);
        Axes["AxisX"].MotMoveAbs(pos.X);
        Axes["AxisY"].MotMoveAbs(pos.Y);
        Axes["AxisZ"].MotMoveAbs(pos.Z);

        bool arrived = await WaitUntilAsync(
            () => Axes["AxisX"].Wait() && Axes["AxisY"].Wait() && Axes["AxisZ"].Wait(),
            _config.MoveTimeout, ct);

        if (!arrived)
            _logger.Warn($"[S03] {slotName} move timeout (positions not taught yet)");

        // STEP 2: Light on
        _light!.SetValue(_config.LightChannel, _config.LightIntensity);
        await Task.Delay(_config.LightStabilizeMs, ct);

        // STEP 3: Camera capture
        _cameraManager!.CameraStart(_cameraKey);
        await Task.Delay(_config.CaptureWaitMs, ct);
        var image = _cameraManager.GetCameraImage(_cameraKey);

        if (image == null)
        {
            _logger.Warn($"[S03] {slotName} capture failed, treat as NG");
            _light.SetValue(_config.LightChannel, 0);
            return (0, true, "", null);
        }

        // STEP 4: Save image (optional)
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

        // STEP 5: Light off
        _light.SetValue(_config.LightChannel, 0);

        // STEP 6: Image measurement (Stub: returns 0.50)
        double measuredValue = ImageMeasurer.Measure(image, slotName);

        // STEP 7: NG judgment
        bool isNg = measuredValue < _config.NgThresholdLow
                 || measuredValue > _config.NgThresholdHigh;

        _logger.Debug($"[S03] {slotName} done Value={measuredValue:F4} isNg={isNg}");
        return (measuredValue, isNg, imagePath, null);
        // TODO: convert HImage to BitmapSource for real mode display
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
                axis.ResetError();
                axis.SetSVON(true);
                bool ready = await WaitUntilAsync(() => axis.GetRDY() && !axis.GetAlarm(), _deviceConnectTimeout, ct);
                if (!ready) return DeviceInitResult.Fail(name, $"{axisName} not ready (RDY={axis.GetRDY()}, Alarm={axis.GetAlarm()})");
                _logger.Debug($"{axisName} ServoOn OK");
            }
            _logger.Info($"{name}: OK"); return DeviceInitResult.Ok(name);
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
            using var socket = new System.Net.Sockets.TcpClient();
            if (!socket.ConnectAsync(_barcodeIp, _barcodePort).Wait(_deviceConnectTimeout))
                return DeviceInitResult.Fail(name, $"Connect {_barcodeIp}:{_barcodePort} timeout");
            if (!socket.Connected)
                return DeviceInitResult.Fail(name, $"Cannot connect {_barcodeIp}:{_barcodePort}");
            _logger.Info($"{name}: OK ({_barcodeIp}:{_barcodePort})"); return DeviceInitResult.Ok(name);
        }
        catch (Exception ex) { _logger.Error(ex, $"{name}: FAIL"); return DeviceInitResult.Fail(name, ex.Message, ex); }
    }

    // =========================================
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
            try { _light?.Dispose(); } catch { }
            try { _modbusClient?.Dispose(); } catch { }
            foreach (var axis in Axes.Values)
                try { axis.MotStop(); axis.SetSVON(false); } catch { }
        }
        _logger.Info("MachineController Disposed");
    }
}
