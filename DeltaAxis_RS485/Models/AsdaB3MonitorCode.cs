namespace DeltaAxis_RS485.Models;

/// <summary>
/// Delta ASDA-B3 監視變數代碼定義
/// 用於 P0.002 (面板監視選擇) 或參數映射 (P0.017~P0.020) 設定
/// </summary>
public static class AsdaB3MonitorCode
{
    // ============================
    //  位置相關 (000~006)
    // ============================

    /// <summary>000 (00h) — 回授位置 (PUU)</summary>
    public const ushort FeedbackPosition_PUU = 0x00;

    /// <summary>001 (01h) — 位置命令 (PUU)</summary>
    public const ushort PositionCommand_PUU = 0x01;

    /// <summary>002 (02h) — 追隨誤差 (PUU)，濾波前</summary>
    public const ushort FollowingError_PUU = 0x02;

    /// <summary>003 (03h) — 回授位置 (pulse)</summary>
    public const ushort FeedbackPosition_Pulse = 0x03;

    /// <summary>004 (04h) — 位置命令 (pulse)</summary>
    public const ushort PositionCommand_Pulse = 0x04;

    /// <summary>005 (05h) — 追隨誤差 (pulse)，濾波前</summary>
    public const ushort FollowingError_Pulse = 0x05;

    /// <summary>006 (06h) — 位置命令頻率 (Kpps)，適用 PT/PR 模式</summary>
    public const ushort PositionCommandFrequency = 0x06;

    // ============================
    //  速度相關 (007~009)
    // ============================

    /// <summary>007 (07h) — 速度回授 (0.1 rpm)，經低通濾波</summary>
    public const ushort SpeedFeedback = 0x07;

    /// <summary>008 (08h) — 速度命令 (類比，0.01V)</summary>
    public const ushort SpeedCommandAnalog = 0x08;

    /// <summary>009 (09h) — 速度命令 (整合，0.1 rpm)</summary>
    public const ushort SpeedCommandIntegrated = 0x09;

    // ============================
    //  扭力相關 (010~011)
    // ============================

    /// <summary>010 (0Ah) — 扭力命令 (類比，0.01V)</summary>
    public const ushort TorqueCommandAnalog = 0x0A;

    /// <summary>011 (0Bh) — 扭力命令 (整合，%)</summary>
    public const ushort TorqueCommandIntegrated = 0x0B;

    // ============================
    //  負載 / 電壓 / 溫度 (012~016)
    // ============================

    /// <summary>012 (0Ch) — 平均負載率 (%，每 20ms 移動平均)</summary>
    public const ushort AverageLoadRate = 0x0C;

    /// <summary>013 (0Dh) — 峰值負載率 (%)</summary>
    public const ushort PeakLoadRate = 0x0D;

    /// <summary>014 (0Eh) — DCBus 電壓 (V)</summary>
    public const ushort DcBusVoltage = 0x0E;

    /// <summary>015 (0Fh) — 負載慣量比 (0.1 倍)</summary>
    public const ushort LoadInertiaRatio = 0x0F;

    /// <summary>016 (10h) — IGBT 溫度 (°C)</summary>
    public const ushort IgbtTemperature = 0x10;

    // ============================
    //  共振 / Z 相 (017~018)
    // ============================

    /// <summary>017 (11h) — 共振頻率 (Low word=F2, High word=F1)</summary>
    public const ushort ResonanceFrequency = 0x11;

    /// <summary>018 (12h) — 與 Z 相偏移量 (-4999~+5000)</summary>
    public const ushort ZPhaseOffset = 0x12;

    // ============================
    //  參數映射 (019~026)
    // ============================

    /// <summary>019 (13h) — 映射參數內容 #1 (P0.025 → P0.035)</summary>
    public const ushort MappedParam1 = 0x13;

    /// <summary>020 (14h) — 映射參數內容 #2 (P0.026 → P0.036)</summary>
    public const ushort MappedParam2 = 0x14;

    /// <summary>021 (15h) — 映射參數內容 #3 (P0.027 → P0.037)</summary>
    public const ushort MappedParam3 = 0x15;

    /// <summary>022 (16h) — 映射參數內容 #4 (P0.028 → P0.038)</summary>
    public const ushort MappedParam4 = 0x16;

    /// <summary>023 (17h) — 映射監視變數 #1 (P0.009 → P0.017)</summary>
    public const ushort MappedMonitor1 = 0x17;

    /// <summary>024 (18h) — 映射監視變數 #2 (P0.010 → P0.018)</summary>
    public const ushort MappedMonitor2 = 0x18;

    /// <summary>025 (19h) — 映射監視變數 #3 (P0.011 → P0.019)</summary>
    public const ushort MappedMonitor3 = 0x19;

    /// <summary>026 (1Ah) — 映射監視變數 #4 (P0.012 → P0.020)</summary>
    public const ushort MappedMonitor4 = 0x1A;

    // ============================
    //  其他狀態 (027~055)
    // ============================

    /// <summary>027 (1Bh) — Z 相偏移量 (僅供台達 CNC 使用)</summary>
    public const ushort ZPhaseOffsetCnc = 0x1B;

    /// <summary>028 (1Ch) — 異警碼 (十進位，換算 hex 同 P0.001)</summary>
    public const ushort AlarmCodeDecimal = 0x1C;

    /// <summary>032 (20h) — 位置誤差 (PUU)，濾波後</summary>
    public const ushort PositionError_PUU = 0x20;

    /// <summary>033 (21h) — 位置誤差 (pulse)，濾波後</summary>
    public const ushort PositionError_Pulse = 0x21;

    /// <summary>035 (23h) — 分度座標命令 (PUU)</summary>
    public const ushort IndexCoordinateCommand = 0x23;

    /// <summary>038 (26h) — 電池電壓 (需開啟 P2.069 絕對型功能)</summary>
    public const ushort BatteryVoltage = 0x26;

    /// <summary>039 (27h) — DI 狀態 (整合，Hex)，來源含硬體及 P4.007</summary>
    public const ushort DiStatusIntegrated = 0x27;

    /// <summary>040 (28h) — DO 狀態 (硬體，Hex)</summary>
    public const ushort DoStatusHardware = 0x28;

    /// <summary>041 (29h) — 驅動器狀態 (同 P0.046)</summary>
    public const ushort DriverStatus = 0x29;

    /// <summary>042 (2Ah) — 執行中的 PR 編號</summary>
    public const ushort ActivePrNumber = 0x2A;

    /// <summary>043 (2Bh) — CAP 抓取資料</summary>
    public const ushort CapCaptureData = 0x2B;

    /// <summary>049 (31h) — 脈波命令 CNT</summary>
    public const ushort PulseCommandCount = 0x31;

    /// <summary>051 (33h) — 速度回授 (立即值，0.1 rpm)</summary>
    public const ushort SpeedFeedbackImmediate = 0x33;

    /// <summary>053 (35h) — 扭力命令 (整合，0.1%)</summary>
    public const ushort TorqueCommandIntegrated_01Pct = 0x35;

    /// <summary>054 (36h) — 扭力回授 (0.1%)</summary>
    public const ushort TorqueFeedback = 0x36;

    /// <summary>055 (37h) — 電流回授 (0.01A)</summary>
    public const ushort CurrentFeedback = 0x37;

    // ============================
    //  進階 (056~123)
    // ============================

    /// <summary>056 (38h) — DCBus 電壓 (0.1V)</summary>
    public const ushort DcBusVoltage_01V = 0x38;

    /// <summary>064 (40h) — PR 命令終點暫存器</summary>
    public const ushort PrCommandEndpoint = 0x40;

    /// <summary>065 (41h) — PR 命令輸出暫存器</summary>
    public const ushort PrCommandOutput = 0x41;

    /// <summary>067 (43h) — PR 目標速度 (PPS)</summary>
    public const ushort PrTargetSpeed = 0x43;

    /// <summary>072 (48h) — 速度命令 (類比，0.1 rpm)</summary>
    public const ushort SpeedCommandAnalog_01Rpm = 0x48;

    /// <summary>081 (51h) — 同步抓取修正軸脈波輸入增量</summary>
    public const ushort SyncCapturePulseIncrement = 0x51;

    /// <summary>082 (52h) — 執行中的 PR 編號 (供 HMC，適用 -F 機種)</summary>
    public const ushort ActivePrNumberHmc = 0x52;

    /// <summary>084 (54h) — 同步抓取修正軸誤差脈波數</summary>
    public const ushort SyncCaptureErrorPulse = 0x54;

    /// <summary>091 (5Bh) — 分度座標回授 (PUU)</summary>
    public const ushort IndexCoordinateFeedback = 0x5B;

    /// <summary>096 (60h) — 驅動器韌體版本 (Low=DSP, High=CPLD)</summary>
    public const ushort FirmwareVersion = 0x60;

    /// <summary>111 (6Fh) — 驅動器伺服錯誤碼 (僅伺服控制迴路)</summary>
    public const ushort ServoErrorCode = 0x6F;

    /// <summary>112 (70h) — CANopen SYNC TS (未濾波，μs)</summary>
    public const ushort CanopenSyncTsRaw = 0x70;

    /// <summary>113 (71h) — CANopen SYNC TS (經濾波，μs)</summary>
    public const ushort CanopenSyncTsFiltered = 0x71;

    /// <summary>119 (77h) — EtherCAT 狀態機 (1=Init, 2=Pre-OP, 4=Safe-OP, 8=OP)</summary>
    public const ushort EtherCatState = 0x77;

    /// <summary>120 (78h) — 通訊錯誤率 (持續累加代表受干擾)</summary>
    public const ushort CommErrorRate = 0x78;

    /// <summary>123 (7Bh) — 面板監視傳回值</summary>
    public const ushort PanelMonitorReturn = 0x7B;
}