using System;
using System.Threading;
using System.Threading.Tasks;
using Synpower.Lighting.Domain;
// 你的命名空間請依實際檔案：假設 OPT_Controller 在 FoupInspecMachine.Models
using FoupInspecMachine.Models;

namespace Synpower.Lighting.Infrastructure
{
    public sealed class OptDeviceAdapter : ILightDeviceController
    {
        private readonly string _port;
        private readonly int _baud;
        private OPT_Controller _opt;

        public OptDeviceAdapter(string port, int baud = 115200)
        {
            _port = port; _baud = baud;
            _opt = new OPT_Controller(_port);
        }

        public Task OpenAsync(CancellationToken ct = default)
        {
           
            _opt.Open();
            return Task.CompletedTask;
        }

        public Task CloseAsync()
        {
            _opt?.Close();
            return Task.CompletedTask;
        }

        public Task SetIntensityPercentAsync(int deviceChannel, int percent, CancellationToken ct = default)
        {
            // 你的 OPT_Controller 已支援百分比 SetValue(channel, percent)
            _opt.SetValue(deviceChannel, Math.Clamp(percent, 0, 100));
            return Task.CompletedTask;
        }

        public Task TurnOnAsync(int deviceChannel, CancellationToken ct = default)
            => SetIntensityPercentAsync(deviceChannel, 100, ct);

        public Task TurnOffAsync(int deviceChannel, CancellationToken ct = default)
            => SetIntensityPercentAsync(deviceChannel, 0, ct);

        public ValueTask DisposeAsync()
        {
            _opt?.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
