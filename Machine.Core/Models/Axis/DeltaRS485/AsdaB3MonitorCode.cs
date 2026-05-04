namespace Machine.Core.Models.Axis.DeltaRS485
{
    /// <summary>
    /// Delta ASDA-B3 監視變數代碼定義
    /// 用於 P0.002 (面板監視顯示) 及參數映射 (P0.017~P0.020) 設定
    /// </summary>
    public static class AsdaB3MonitorCode
    {
        /// <summary>000 (00h) — 回授位置 (PUU)</summary>
        public const ushort FeedbackPosition_PUU = 0x00;

        /// <summary>028 (1Ch) — 警報碼 (十進制)</summary>
        public const ushort AlarmCodeDecimal = 0x1C;

        /// <summary>039 (27h) — DI 狀態 (輸出整合, Hex)</summary>
        public const ushort DiStatusIntegrated = 0x27;

        /// <summary>040 (28h) — DO 狀態 (硬體, Hex)</summary>
        public const ushort DoStatusHardware = 0x28;
    }
}
