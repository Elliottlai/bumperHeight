using System;
using System.Threading;
using System.Threading.Tasks;
using Synpower.Lighting.Domain;
using LightControl;   // 你的 VLP_LSG2CH 所在 namespace（依專案調整）

namespace Synpower.Lighting.Infrastructure
{
    /// <summary>
    /// VS 光源控制器適配器
    /// 採用 LSG 2CH UART Protocol (PDF)
    /// 並符合 ILightDeviceController 標準介面
    /// </summary>
    public sealed class VSDeviceAdapter : ILightDeviceController
    {
        private readonly string _port;
        private readonly int _baud;
        private VLP_LSG2CH _ctrl;

        public VSDeviceAdapter(string port, int baud = 115200)
        {
            _port = port;
            _baud = baud;
        }

        // ---------------------------------------------------------
        //  Open / Close
        // ---------------------------------------------------------
        public Task OpenAsync(CancellationToken ct = default)
        {
            _ctrl = new VLP_LSG2CH();
            _ctrl.Connect(_port, _baud);
            return Task.CompletedTask;
        }

        public Task CloseAsync()
        {
            _ctrl?.Disconnect();
            return Task.CompletedTask;
        }

        // ---------------------------------------------------------
        //  Set Intensity (percent 0~100)
        // ---------------------------------------------------------
        public async Task SetIntensityPercentAsync(int deviceChannel, int percent, CancellationToken ct = default)
        {
            if (deviceChannel != 1 && deviceChannel != 2)
                throw new ArgumentException("deviceChannel must be 1 or 2");

            percent = Math.Clamp(percent, 0, 100);

            // 呼叫 LSG2CH 單通道保留模式
            await _ctrl.SetBrightnessAsync(deviceChannel, (byte)percent);
        }

        // ---------------------------------------------------------
        // Turn ON / OFF
        // ---------------------------------------------------------
        public Task TurnOnAsync(int deviceChannel, CancellationToken ct = default)
            => SetIntensityPercentAsync(deviceChannel, 100, ct);

        public Task TurnOffAsync(int deviceChannel, CancellationToken ct = default)
            => SetIntensityPercentAsync(deviceChannel, 0, ct);

        // ---------------------------------------------------------
        // Dispose
        // ---------------------------------------------------------
        public ValueTask DisposeAsync()
        {
            _ctrl?.Disconnect();
            return ValueTask.CompletedTask;
        }
    }
}
