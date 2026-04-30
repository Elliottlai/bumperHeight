namespace DeltaAxis_RS485.Models;

/// <summary>
/// Delta ASDA-B3 驅動器 Modbus 寄存器位址對照表
/// 
/// 說明：
/// 1. 本表以 ASDA-B3 手冊的參數地址格式整理
/// 2. 32-bit 參數以 Low Word 起始位址表示
/// 3. 讀寫 32-bit 時，請同時操作 Low / High 兩個 16-bit register
/// 4. 少數未完全核實之位址仍保留 TODO，避免誤寫
/// </summary>
public static class AsdaB3RegisterMap
{
    // ============================
    //  P0 群組 — 監控 / Absolute
    // ============================

    /// <summary>P0.049 — 更新 absolute position（待再核）</summary>
    public const ushort P0_049_UpdateAbsPosition = 0x0062;   // TODO: 建議再與原廠手冊表格逐筆確認

    /// <summary>P0.050 — Absolute 狀態</summary>
    public const ushort P0_050_AbsStatus = 0x0064;

    /// <summary>P0.051 — 多圈位置 (32-bit, Low word)</summary>
    public const ushort P0_051_MultiTurnPos_Low = 0x0066;

    /// <summary>P0.051 — 多圈位置 (32-bit, High word)</summary>
    public const ushort P0_051_MultiTurnPos_High = 0x0067;

    /// <summary>P0.052 — 單圈位置 (32-bit, Low word)</summary>
    public const ushort P0_052_SingleTurnPos_Low = 0x0068;

    /// <summary>P0.052 — 單圈位置 (32-bit, High word)</summary>
    public const ushort P0_052_SingleTurnPos_High = 0x0069;

    // ============================
    //  P1 群組 — 基本參數
    // ============================

    /// <summary>P1.001 — 控制模式</summary>
    public const ushort P1_001_ControlMode = 0x0102;         // 推定值，建議實機再核

    /// <summary>P1.054 — 到位容許誤差</summary>
    public const ushort P1_054_InPosWindow = 0x016C;         // 推定值，建議實機再核

    // ============================
    //  P2 群組 — 擴充 / Absolute
    // ============================

    /// <summary>P2.008 — 特殊參數寫保護解除</summary>
    public const ushort P2_008_WriteProtect = 0x0210;

    /// <summary>P2.070 — Absolute 資料讀取設定</summary>
    public const ushort P2_070_AbsReadSetting = 0x028C;

    /// <summary>P2.071 — 建立 absolute origin</summary>
    public const ushort P2_071_BuildAbsOrigin = 0x028E;      // 推定值，建議實機再核

    // ============================
    //  P3 群組 — 通訊參數
    // ============================

    /// <summary>P3.000 — RS485 站號</summary>
    public const ushort P3_000_StationId = 0x0300;

    /// <summary>P3.001 — 鮑率</summary>
    public const ushort P3_001_BaudRate = 0x0302;            // 推定值

    /// <summary>P3.002 — 協定</summary>
    public const ushort P3_002_Protocol = 0x0304;            // 推定值

    /// <summary>P3.003 — 通訊錯誤處理</summary>
    public const ushort P3_003_CommErrHandle = 0x0306;       // 推定值

    /// <summary>P3.004 — 通訊 Timeout</summary>
    public const ushort P3_004_CommTimeout = 0x0308;         // 推定值

    /// <summary>P3.006 — DI 控制切換</summary>
    public const ushort P3_006_DiControl = 0x030C;

    /// <summary>P3.007 — 回應延遲</summary>
    public const ushort P3_007_ResponseDelay = 0x030E;       // 推定值

    // ============================
    //  P5 群組 — PR / Motion
    // ============================

    /// <summary>P5.004 — Homing method</summary>
    public const ushort P5_004_HomingMethod = 0x0508;        // 推定值

    /// <summary>P5.005 — 高速 Homing 速度 (32-bit, Low word)</summary>
    public const ushort P5_005_HighSpeedHoming_Low = 0x050A;

    /// <summary>P5.005 — 高速 Homing 速度 (32-bit, High word)</summary>
    public const ushort P5_005_HighSpeedHoming_High = 0x050B;

    /// <summary>P5.006 — 低速 Homing 速度 (32-bit, Low word)</summary>
    public const ushort P5_006_LowSpeedHoming_Low = 0x050C;

    /// <summary>P5.006 — 低速 Homing 速度 (32-bit, High word)</summary>
    public const ushort P5_006_LowSpeedHoming_High = 0x050D;

    /// <summary>P5.007 — PR command trigger（寫 1 = 執行 PR#1）</summary>
    public const ushort P5_007_PrTrigger = 0x050E;

    /// <summary>P5.008 — 正向軟限位 (32-bit, Low word)</summary>
    public const ushort P5_008_PosLimit_Low = 0x0510;

    /// <summary>P5.008 — 正向軟限位 (32-bit, High word)</summary>
    public const ushort P5_008_PosLimit_High = 0x0511;

    /// <summary>P5.009 — 反向軟限位 (32-bit, Low word)</summary>
    public const ushort P5_009_NegLimit_Low = 0x0512;

    /// <summary>P5.009 — 反向軟限位 (32-bit, High word)</summary>
    public const ushort P5_009_NegLimit_High = 0x0513;

    /// <summary>P5.020 — Acc/Dec Time #1</summary>
    public const ushort P5_020_AccDecTime1 = 0x0528;         // 推定值

    /// <summary>P5.040 — Delay Time #1</summary>
    public const ushort P5_040_DelayTime1 = 0x0550;          // 推定值

    /// <summary>P5.061 — Target Speed #1 (32-bit, Low word)</summary>
    public const ushort P5_061_TargetSpeed1_Low = 0x057A;

    /// <summary>P5.061 — Target Speed #1 (32-bit, High word)</summary>
    public const ushort P5_061_TargetSpeed1_High = 0x057B;

    // ============================
    //  P6 群組 — PR Path Definition
    // ============================

    /// <summary>P6.000 — Homing definition (32-bit, Low word)</summary>
    public const ushort P6_000_HomingDef_Low = 0x0600;

    /// <summary>P6.000 — Homing definition (32-bit, High word)</summary>
    public const ushort P6_000_HomingDef_High = 0x0601;

    /// <summary>P6.002 — PR#1 path definition (32-bit, Low word)</summary>
    public const ushort P6_002_Pr1PathDef_Low = 0x0604;

    /// <summary>P6.002 — PR#1 path definition (32-bit, High word)</summary>
    public const ushort P6_002_Pr1PathDef_High = 0x0605;

    /// <summary>P6.003 — PR#1 目標位置 (32-bit, Low word)</summary>
    public const ushort P6_003_Pr1TargetPos_Low = 0x0606;

    /// <summary>P6.003 — PR#1 目標位置 (32-bit, High word)</summary>
    public const ushort P6_003_Pr1TargetPos_High = 0x0607;

    // ============================
    //  狀態暫存器（未完全核實）
    // ============================

    /// <summary>
    /// 驅動器狀態字
    /// 注意：此位址你原本自定為 0x0900，但本次未核到官方依據，先保留 TODO。
    /// </summary>
    public const ushort StatusWord = 0x0900; // TODO: 請依正式 Modbus map 確認

    /// <summary>警報代碼暫存器</summary>
    public const ushort AlarmCode = 0x0901;  // TODO: 請依正式 Modbus map 確認

    /// <summary>DI 虛擬暫存器</summary>
    public const ushort DiVirtualReg = 0x0902; // TODO: 請依正式 Modbus map 確認

    // ============================
    //  Status Word 位元遮罩（未完全核實）
    // ============================

    public const ushort StatusBit_ServoOn = 0x0001;      // TODO: bit 位置待確認
    public const ushort StatusBit_Alarm = 0x0008;        // TODO: bit 位置待確認
    public const ushort StatusBit_InPosition = 0x0010;   // TODO: bit 位置待確認

    // ============================
    //  常數
    // ============================

    /// <summary>P1.001 設為 PR mode 的值</summary>
    public const ushort ControlMode_PR = 0x0001; // 建議再依模式值表確認

    /// <summary>P2.008 解鎖值</summary>
    public const ushort WriteProtectUnlock = 271;

    /// <summary>PR#1 觸發值</summary>
    public const ushort PrNumber_1 = 1;
}