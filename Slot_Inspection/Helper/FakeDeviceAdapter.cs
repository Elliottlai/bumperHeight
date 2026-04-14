using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Synpower.Lighting.Domain;

namespace Synpower.Lighting.Infrastructure
{
    /// <summary>
    /// 模擬光源：不連任何硬體；只在記憶體保存各通道的百分比值。
    /// 在需要時可加上事件回報或隨機抖動來模擬雜訊/延遲。
    /// </summary>
    public sealed class FakeDeviceAdapter : ILightDeviceController
    {
        private readonly string _name;              // 方便識別，例如 "OPT#1(Fake)"
        private readonly TimeSpan _ioLatency;       // 模擬通訊延遲
        private volatile bool _opened;
        private readonly ConcurrentDictionary<int, int> _channels = new(); // deviceChannel -> 0..100

        public FakeDeviceAdapter(string name, TimeSpan? ioLatency = null)
        {
            _name = name;
            _ioLatency = ioLatency ?? TimeSpan.FromMilliseconds(10);
        }

        public async Task OpenAsync(CancellationToken ct = default)
        {
            await Task.Delay(_ioLatency, ct);
            _opened = true;
            // 可在這裡初始化預設值
        }

        public async Task CloseAsync()
        {
            await Task.Delay(_ioLatency);
            _opened = false;
        }

        public async Task SetIntensityPercentAsync(int deviceChannel, int percent, CancellationToken ct = default)
        {
            if (!_opened) throw new InvalidOperationException($"{_name} not opened.");
            percent = Math.Clamp(percent, 0, 100);
            await Task.Delay(_ioLatency, ct); // 模擬 I/O 時間
            _channels[deviceChannel] = percent;
            // 你也可以 Console.WriteLine($"{_name} ch{deviceChannel}={percent}%");
        }

        public Task TurnOnAsync(int deviceChannel, CancellationToken ct = default)
            => SetIntensityPercentAsync(deviceChannel, 100, ct);

        public Task TurnOffAsync(int deviceChannel, CancellationToken ct = default)
            => SetIntensityPercentAsync(deviceChannel, 0, ct);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        // （可選）提供讀回：方便單元測試
        public int GetPercentOrDefault(int deviceChannel) =>
            _channels.TryGetValue(deviceChannel, out var v) ? v : 0;
    }
}
