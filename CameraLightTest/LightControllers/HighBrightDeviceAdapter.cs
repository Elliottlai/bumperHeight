using System;
using System.Threading;
using System.Threading.Tasks;
using CameraLightTest.LightControllers;
using Synpower.Lighting.Domain;

namespace CameraLightTest.LightControllers
{
    /// <summary>
    /// HighBright 光源適配器
    /// 實作 ILightDeviceController，對齊 VSDeviceAdapter 風格
    /// </summary>
    public sealed class HighBrightDeviceAdapter : ILightDeviceController
    {
        private readonly string _portName;
        private readonly int    _baudRate;
        private HighBright_Controller? _ctrl;

        public HighBrightDeviceAdapter(string portName, int baudRate = 9600)
        {
            _portName = portName;
            _baudRate = baudRate;
        }

        // ---------------------------------------------------------
        //  Open / Close
        // ---------------------------------------------------------
        public Task OpenAsync(CancellationToken ct = default)
        {
            _ctrl = new HighBright_Controller(_portName, _baudRate);
            _ctrl.Connect();
            return Task.CompletedTask;
        }

        public Task CloseAsync()
        {
            _ctrl?.Disconnect();
            return Task.CompletedTask;
        }

        // ---------------------------------------------------------
        //  Set Intensity (percent 0~100) → bool
        // ---------------------------------------------------------
        public Task SetIntensityPercentAsync(int deviceChannel, int percent, CancellationToken ct = default)
        {
            if (_ctrl is null)
                throw new InvalidOperationException("尚未呼叫 OpenAsync");

            percent = Math.Clamp(percent, 0, 100);
            bool ok = _ctrl.SetValue(deviceChannel, percent);

            // 回傳 false 時記錄但不拋出，讓上層 LightService 的節流機制繼續運作
            _ = ok;

            return Task.CompletedTask;
        }

        // ---------------------------------------------------------
        //  Turn ON / OFF
        // ---------------------------------------------------------
        public Task TurnOnAsync(int deviceChannel, CancellationToken ct = default)
            => SetIntensityPercentAsync(deviceChannel, 100, ct);

        public Task TurnOffAsync(int deviceChannel, CancellationToken ct = default)
            => SetIntensityPercentAsync(deviceChannel, 0, ct);

        // ---------------------------------------------------------
        //  Dispose
        // ---------------------------------------------------------
        public ValueTask DisposeAsync()
        {
            _ctrl?.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}