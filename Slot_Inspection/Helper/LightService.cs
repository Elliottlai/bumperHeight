using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Synpower.Lighting.Domain;

namespace Synpower.Lighting.Application
{
    public sealed class LightService : IAsyncDisposable
    {
        private readonly IReadOnlyDictionary<ControllerKey, ILightDeviceController> _controllers;
        private readonly IReadOnlyDictionary<LightChannelId, LightChannelConfig> _cfg;
        private readonly Dictionary<ControllerKey, SemaphoreSlim> _locks = new();
        private readonly TimeSpan _spacing = TimeSpan.FromMilliseconds(80);

        public LightService(
            IEnumerable<(ControllerKey key, ILightDeviceController ctrl)> controllerBindings,
            IEnumerable<LightChannelConfig> channelConfigs)
        {
            _controllers = controllerBindings.ToDictionary(x => x.key, x => x.ctrl);
            foreach (var k in _controllers.Keys) _locks[k] = new SemaphoreSlim(1, 1);
            _cfg = channelConfigs.ToDictionary(c => c.UiId, c => c);
        }

        public async Task OpenAllAsync(CancellationToken ct = default)
        {
            foreach (var c in _controllers.Values) await c.OpenAsync(ct);
        }

        public async Task CloseAllAsync()
        {
            foreach (var c in _controllers.Values) await c.CloseAsync();
        }

        // UI 端用百分比設定
        public async Task SetByUiAsync(LightChannelId uiId, int percent, CancellationToken ct = default)
        {
            if (!_cfg.TryGetValue(uiId, out var cfg)) return;

            var p = Math.Clamp(percent, 0, 100);
            if (cfg.CapPercent is int cap) p = Math.Min(p, cap);

            var key = cfg.Controller;
            var sem = _locks[key];

            await sem.WaitAsync(ct);
            try
            {
                await _controllers[key].SetIntensityPercentAsync(cfg.DeviceChannel, p, ct);
                await Task.Delay(_spacing, ct); // 簡單節流，避免指令過密
            }
            finally { sem.Release(); }
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var c in _controllers.Values) await c.DisposeAsync();
        }
    }
}

