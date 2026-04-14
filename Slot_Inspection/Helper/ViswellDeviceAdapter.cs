using System;
using System.Threading;
using System.Threading.Tasks;
using Synpower.Lighting.Domain;
// 你的命名空間請依實際檔案：假設 cLight_SerialPortControlBox 在 Machine.Core
using Machine.Core;

namespace Synpower.Lighting.Infrastructure
{



    public class ViswellDeviceAdapter : ILightDeviceController
    {

        private readonly string _port;
        private readonly int _channelCount;
        private readonly int _baud;
        private cLight_SerialPortControlBox _box;

        public ViswellDeviceAdapter(string port, int channelCount, int baud = 115200)
        {
            _port = port; _channelCount = channelCount; _baud = baud;
        }

        public Task OpenAsync(CancellationToken ct = default)
        {
            _box = new cLight_SerialPortControlBox(_port, _channelCount);
            _box.BaudRate = _baud;
            return Task.CompletedTask;
        }

        public Task CloseAsync()
        {
            _box?.Dispose(); // 內部會 TurnOff 並釋放 SerialPort
            return Task.CompletedTask;
        }
        private object Lock = new object();
        public Task SetIntensityPercentAsync(int deviceChannel, int percent, CancellationToken ct = default)
        {
            try
            {

                Monitor.Enter(Lock);
                percent = Math.Clamp(percent, 0, 100);
                int max = _box.MaxLevel; // 例如 1023
                int dev = (int)Math.Round(percent / 100.0 * max);
                _box.SetLuminance(deviceChannel, dev); // 原方法非同步無回傳，可配合 Service 節流
            }
            finally
            {
                Monitor.Exit(Lock);
            }
            return Task.CompletedTask;
        }

        public Task TurnOnAsync(int deviceChannel, CancellationToken ct = default)
            => SetIntensityPercentAsync(deviceChannel, 100, ct);

        public Task TurnOffAsync(int deviceChannel, CancellationToken ct = default)
            => SetIntensityPercentAsync(deviceChannel, 0, ct);

        public ValueTask DisposeAsync()
        {
            _box?.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
