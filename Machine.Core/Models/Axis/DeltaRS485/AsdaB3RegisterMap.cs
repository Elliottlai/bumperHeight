namespace Machine.Core.Models.Axis.DeltaRS485
{
    /// <summary>
    /// Delta ASDA-B3 驅動器 Modbus 暫存器位址常數表
    /// </summary>
    public static class AsdaB3RegisterMap
    {
        // ============================
        //  P0 群組 — 監視 / Absolute
        // ============================
        public const ushort P0_000_Version = 0x0000;

        /// <summary>P0.045 — DI 訊號整合狀態 (16-bit, HEX)</summary>
        public const ushort P0_045_DiStatus = 0x005A;

        /// <summary>P0.046 — 驅動器狀態輸出 (16-bit, HEX)</summary>
        public const ushort P0_046_DriverStatus = 0x005C;

        /// <summary>P0.050 — 絕對型座標系統狀態 (16-bit, HEX)</summary>
        public const ushort P0_050_AbsStatus = 0x0064;

        /// <summary>P0.051 — 多圈絕對位置 (32-bit, Low word)</summary>
        public const ushort P0_051_MultiTurnPos_Low = 0x0066;

        /// <summary>P0.052 — 單圈絕對位置 (32-bit, Low word)</summary>
        public const ushort P0_052_SingleTurnPos_Low = 0x0068;

        // ============================
        //  P0 群組 — 監視變數映射
        // ============================
        public const ushort P0_009_MappedMonitorValue1 = 0x0012;
        public const ushort P0_010_MappedMonitorValue2 = 0x0014;
        public const ushort P0_011_MappedMonitorValue3 = 0x0016;
        public const ushort P0_012_MappedMonitorValue4 = 0x0018;

        public const ushort P0_017_MappedMonitorSel1 = 0x0022;
        public const ushort P0_018_MappedMonitorSel2 = 0x0024;
        public const ushort P0_019_MappedMonitorSel3 = 0x0026;
        public const ushort P0_020_MappedMonitorSel4 = 0x0028;

        // ============================
        //  P2 群組 — 擴充 / Absolute
        // ============================
        public const ushort P2_008_WriteProtect = 0x0210;
        public const ushort P2_030_AuxFunction = 0x023C;
        public const ushort AuxFunction_NoEepromSave = 5;
        public const ushort P2_071_BuildAbsOrigin = 0x028E;

        // ============================
        //  P3 群組 — 通訊參數
        // ============================
        public const ushort P3_006_DiControl = 0x030C;

        // ============================
        //  P4 群組 — DI/DO 功能
        // ============================
        public const ushort P4_006_SoftwareDO = 0x040C;
        public const ushort P4_007_SoftwareDI = 0x040E;

        public const ushort P5_005_HighSpeedHoming_Low = 0x050A; // 高速歸原點速度 (32-bit Low)
        public const ushort P5_006_LowSpeedHoming_Low = 0x050C; // 低速歸原點速度 (32-bit Low)

        // ============================
        //  P5 群組 — PR / Motion
        // ============================
        public const ushort P5_007_PrTrigger = 0x050E;
        public const ushort P5_020_AccDecTime0 = 0x0528;
        public const ushort P5_040_DelayTime0 = 0x0550;
        public const ushort P5_060_TargetSpeed0 = 0x0578;

        // ============================
        //  P6 群組 — PR Path Definition
        // ============================
        public const ushort P6_002_Pr1PathDef_Low = 0x0604;
        public const ushort P6_003_Pr1TargetPos_Low = 0x0606;

        // ============================
        //  狀態暫存器
        // ============================
        public const ushort StatusWord = P4_006_SoftwareDO;
        public const ushort AlarmCode = 0x0002;

        // ============================
        //  常數
        // ============================
        public const ushort WriteProtectUnlock = 271;
        public const ushort PrNumber_1 = 1;
        public const ushort PrTrigger_Stop = 1000;

        // ============================
        //  DI 位元遮罩
        // ============================
        public const ushort DI1_Bit = 0x0001;
        public const ushort DI2_Bit = 0x0002;
        public const ushort DI3_Bit = 0x0004;
        public const ushort DI4_Bit = 0x0008;
        public const ushort DI5_Bit = 0x0010;
        public const ushort DI6_Bit = 0x0020;
        public const ushort DI7_Bit = 0x0040;
        public const ushort DI8_Bit = 0x0080;
        public const ushort DI9_Bit = 0x0100;

        // ============================
        //  Status Bit 遮罩
        // ============================
        public const ushort StatusBit_Brake = 0x0010;
    }
}
