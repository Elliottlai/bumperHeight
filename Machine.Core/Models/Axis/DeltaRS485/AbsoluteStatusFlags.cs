namespace Machine.Core.Models.Axis.DeltaRS485
{
    /// <summary>
    /// P0.050 絕對型座標系統狀態 (0x0064H, 16-bit)
    /// 各 bit 為 0 表示正常，1 表示異常
    /// </summary>
    [Flags]
    public enum AbsoluteStatusFlags : ushort
    {
        None = 0,

        /// <summary>Bit 0: 絕對位置狀態異常</summary>
        AbsolutePosition = 1 << 0,

        /// <summary>Bit 1: 電池電壓狀態異常</summary>
        BatteryVoltage = 1 << 1,

        /// <summary>Bit 2: 絕對圈數狀態異常</summary>
        AbsoluteRevolution = 1 << 2,

        /// <summary>Bit 3: PUU 狀態異常</summary>
        Puu = 1 << 3,

        /// <summary>Bit 4: 絕對座標狀態異常</summary>
        AbsoluteCoordinate = 1 << 4,
    }
}
