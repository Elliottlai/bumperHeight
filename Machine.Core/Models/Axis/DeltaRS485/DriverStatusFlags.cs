namespace Machine.Core.Models.Axis.DeltaRS485
{
    /// <summary>
    /// Delta ASDA-B3 驅動器狀態 (P0.046) 位元定義
    /// 通訊位址: 005CH
    /// </summary>
    [Flags]
    public enum DriverStatusFlags : ushort
    {
        None = 0,

        /// <summary>Bit 0 — SRDY (伺服準備)</summary>
        ServoReady = 1 << 0,

        /// <summary>Bit 1 — SON (伺服啟動)</summary>
        ServoOn = 1 << 1,

        /// <summary>Bit 2 — ZSPD (零速偵測輸出)</summary>
        ZeroSpeed = 1 << 2,

        /// <summary>Bit 3 — TSPD (目標速度到達)</summary>
        TargetSpeedReached = 1 << 3,

        /// <summary>Bit 4 — TPOS (目標位置到達)</summary>
        TargetPositionReached = 1 << 4,

        /// <summary>Bit 5 — TQL (扭矩限制)</summary>
        TorqueLimited = 1 << 5,

        /// <summary>Bit 6 — ALRM (伺服警報)</summary>
        Alarm = 1 << 6,

        /// <summary>Bit 7 — BRKR (電磁煞車輸出)</summary>
        BrakeRelease = 1 << 7,

        /// <summary>Bit 8 — HOME (原點復歸完成)</summary>
        HomeComplete = 1 << 8,

        /// <summary>Bit 9 — OLW (過負載預警)</summary>
        OverloadWarning = 1 << 9,

        /// <summary>Bit 10 — WARN (伺服警告)</summary>
        Warning = 1 << 10,
    }
}
