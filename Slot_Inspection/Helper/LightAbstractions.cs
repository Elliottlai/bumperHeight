using System;
using System.Threading;
using System.Threading.Tasks;

namespace Synpower.Lighting.Domain
{
    // 控制器種類
    public enum LightKind { OPT, Viswell , VST  }

    // 唯一控制器鍵 (e.g. "OPT#1", "LineBox#1")
    public readonly record struct ControllerKey(string Value)
    {
        public override string ToString() => Value;
        public static ControllerKey Of(LightKind kind, int id) => new($"{kind}#{id}");
    }

    // UI 端通道識別（你可自行擴充）
    public enum LightChannelId
    {
        DoorBack = 1,
        Foup = 2,
        //BackLight = 3,
        //Opt4 = 4,

        Handle = 101,
        FoupSide = 102,
        FoupTop = 103,
        HandleCrack = 104,

        Filter = 201,
        Valve1 = 202,
        Valve2 = 203,
        Opt24 = 204,


        DoorSide = 301,
        FoupSideOuter = 302,
        //OptTop3 = 302,
        //BackLight3 = 303,
        //Opt43 = 304,

        Foup1 = 401,
        Foup2 = 402,
        
        VSTFront = 501,
        VSTBack = 502,

        LineRight = 601,
        LineLeft = 602,

    }

    // UI 通道 → 裝置通道對應與規則
    public sealed record LightChannelConfig(
        LightChannelId UiId,
        string DisplayName,
        ControllerKey Controller,
        int DeviceChannel,
        int? CapPercent = null // 例如 BackLight 上限 9%
    );

    // 統一控制器介面（以百分比 0..100 操作）
    public interface ILightDeviceController : IAsyncDisposable
    {
        Task OpenAsync(CancellationToken ct = default);
        Task CloseAsync();

        Task SetIntensityPercentAsync(int deviceChannel, int percent, CancellationToken ct = default);
        Task TurnOnAsync(int deviceChannel, CancellationToken ct = default);
        Task TurnOffAsync(int deviceChannel, CancellationToken ct = default);
    }
}
