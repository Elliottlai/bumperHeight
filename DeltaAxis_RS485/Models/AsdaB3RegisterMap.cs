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
    //  P0 群組 — 監視 / Absolute
    // ============================
    public const ushort P0_000_Version = 0x0000;

  
    /// <summary>P0.045 — DI 訊號狀態顯示 (16-bit, HEX, 各 bit 對應 DI1~DI14)</summary>
    public const ushort P0_045_DiStatus = 0x005A;

    /// <summary>P0.046 — 驅動器數位輸出(DO)訊號狀態顯示 (16-bit, HEX)</summary>
    public const ushort P0_046_DriverStatus = 0x005C;

    /// <summary>P0.050 — 絕對型座標系統狀態 (16-bit, HEX, 0x0000~0x001F)</summary>
    public const ushort P0_050_AbsStatus = 0x0064;

    /// <summary>P0.051 — 多圈絕對位置 (32-bit, Low word)</summary>
    public const ushort P0_051_MultiTurnPos_Low = 0x0066;

    /// <summary>P0.052 — 單圈絕對位置 (32-bit, Low word)</summary>
    public const ushort P0_052_SingleTurnPos_Low = 0x0068;

    // ============================
    //  P0 群組 — 監視變數映射 (通訊讀取用)
    // ============================

    /// <summary>P0.009 — 映射監視變數 #1 回傳值 (對應 P0.017 指定的監視代碼)</summary>
    public const ushort P0_009_MappedMonitorValue1 = 0x0012;

    /// <summary>P0.010 — 映射監視變數 #2 回傳值 (對應 P0.018 指定的監視代碼)</summary>
    public const ushort P0_010_MappedMonitorValue2 = 0x0014;

    /// <summary>P0.011 — 映射監視變數 #3 回傳值 (對應 P0.019 指定的監視代碼)</summary>
    public const ushort P0_011_MappedMonitorValue3 = 0x0016;

    /// <summary>P0.012 — 映射監視變數 #4 回傳值 (對應 P0.020 指定的監視代碼)</summary>
    public const ushort P0_012_MappedMonitorValue4 = 0x0018;

    /// <summary>P0.013 — 映射監視變數 #5 回傳值</summary>
    public const ushort P0_013_MappedMonitorValue5 = 0x001A;

    /// <summary>P0.017 — 映射監視變數 #1 代碼設定 (填入 AsdaB3MonitorCode)</summary>
    public const ushort P0_017_MappedMonitorSel1 = 0x0022;

    /// <summary>P0.018 — 映射監視變數 #2 代碼設定</summary>
    public const ushort P0_018_MappedMonitorSel2 = 0x0024;

    /// <summary>P0.019 — 映射監視變數 #3 代碼設定</summary>
    public const ushort P0_019_MappedMonitorSel3 = 0x0026;

    /// <summary>P0.020 — 映射監視變數 #4 代碼設定</summary>
    public const ushort P0_020_MappedMonitorSel4 = 0x0028;


    /// <summary>P0.021 — 映射監視變數 #4 代碼設定</summary>
    public const ushort P0_021_MappedMonitorSel4 = 0x002A;


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

    /// <summary>
    /// P2.030 — 輔助機能
    /// <para>設定 5: 斷電後不保持參數，防止 EEPROM 連續寫入減短壽命（通訊控制時必須設定）</para>
    /// </summary>
    public const ushort P2_030_AuxFunction = 0x023C;

    /// <summary>P2.030 設定值: 不保持寫入資料，保護 EEPROM</summary>
    public const ushort AuxFunction_NoEepromSave = 5;

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

    /// <summary>
    /// P3.006 — DI 來源控制開關 (16-bit, HEX)
    /// <para>每 1 位元決定 1 個 DI 之信號輸入來源：Bit0~Bit12 對應 DI1~DI13</para>
    /// <para>0: 由外部硬體端子控制；1: 由 P4.007 軟體 DI 控制</para>
    /// </summary>
    public const ushort P3_006_DiControl = 0x030C;

    /// <summary>P3.007 — 回應延遲</summary>
    public const ushort P3_007_ResponseDelay = 0x030E;       // 推定值

    // ============================
    //  P4 群組 — DI/DO 功能
    // ============================

    /// <summary>
    /// P4.006 — 軟體 DO 資料暫存器 (可讀寫, 16-bit, HEX)
    /// <para>Bit0~Bit15 對應 DO code 0x30~0x3F</para>
    /// <para>若 P2.018=0x0130，則 DO1 輸出即為 P4.006 的 bit00 狀態，依此類推</para>
    /// <para>設定範圍: 0x0000~0xFFFF</para>
    /// </summary>
    public const ushort P4_006_SoftwareDO = 0x040C;

    /// <summary>
    /// P4.007 — 數位輸入接點多重功能 (軟體 DI, 16-bit, HEX)
    /// <para>當 P3.006 對應位元為 1 時，DI 來源由此參數控制</para>
    /// <para>Bit0~Bit13 對應 DI1~DI14，設定範圍: 0x0000~0x3FFF</para>
    /// </summary>
    public const ushort P4_007_SoftwareDI = 0x040E;

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
    public const ushort P5_020_AccDecTime0 = 0x0528;         // 推定值

    /// <summary>P5.040 — Delay Time #1</summary>
    public const ushort P5_040_DelayTime0 = 0x0550;          // 推定值

    /// <summary>P5.061 — Target Speed #1 (32-bit, Low word)</summary>
    public const ushort P5_060_TargetSpeed0 = 0x0578;

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

    /// <summary>P6.003 — PR#1 目標位置 (32-bit, 高 word)</summary>
    public const ushort P6_003_Pr1TargetPos_High = 0x0607;

    // ============================
    //  狀態暫存器 — 透過 P4.006 軟體 DO 讀取
    // ============================

    /// <summary>
    /// 驅動器狀態字 — 使用 P4.006 軟體 DO 暫存器
    /// <para>DO1 Bit0: 伺服準備完畢 (Servo Ready, DO code 0x01)</para>
    /// <para>DO2 Bit1: 馬達零速度 (Zero Speed, DO code 0x03)</para>
    /// <para>DO3 Bit2: 原點復歸完成 (Home Complete, DO code 0x09)</para>
    /// <para>DO4 Bit3: 目標位置到達 (In Position, DO code 0x05)</para>
    /// <para>DO5 Bit4: 電磁煞車控制 (Brake, DO code 0x08)</para>
    /// <para>DO6 Bit5: 伺服警示 B contact (Alarm, DO code 0x07)</para>
    /// </summary>
    public const ushort StatusWord = P4_006_SoftwareDO;  // 0x040C

    /// <summary>警報代碼暫存器 (P0.001 推定)</summary>
    public const ushort AlarmCode = 0x0002;  // TODO: 請依正式 Modbus map 確認 P0.001 位址
 
    // ============================
    //  Status Word 位元遮罩（對應 P4.006 DO bit）
    // ============================

    /// <summary>DO1 Bit0 — 伺服準備完畢 (Servo Ready)</summary>
    public const ushort StatusBit_ServoReady = 0x0001;

    /// <summary>DO2 Bit1 — 馬達零速度</summary>
    public const ushort StatusBit_ZeroSpeed = 0x0002;

    /// <summary>DO3 Bit2 — 原點復歸完成</summary>
    public const ushort StatusBit_HomeComplete = 0x0004;

    /// <summary>DO4 Bit3 — 目標位置到達 (In Position)</summary>
    public const ushort StatusBit_InPosition = 0x0008;

    /// <summary>DO5 Bit4 — 電磁煞車控制</summary>
    public const ushort StatusBit_Brake = 0x0010;

    /// <summary>DO6 Bit5 — 伺服警示 (B contact, 0=有警報)</summary>
    public const ushort StatusBit_AlarmB = 0x0020;

    // DO 功能碼
    public const ushort DO_Func_ServoReady = 0x01;
    public const ushort DO_Func_ZeroSpeed = 0x03;
    public const ushort DO_Func_InPosition = 0x05;
    public const ushort DO_Func_Alarm = 0x07;
    public const ushort DO_Func_Brake = 0x08;
    public const ushort DO_Func_HomeComplete = 0x09;

    // ============================
    //  常數
    // ============================

    /// <summary>P1.001 設為 PR mode 的值</summary>
    public const ushort ControlMode_PR = 0x0001; // 建議再依模式值表確認

    /// <summary>P2.008 解鎖值</summary>
    public const ushort WriteProtectUnlock = 271;

    /// <summary>PR#1 觸發值</summary>
    public const ushort PrNumber_1 = 1;

    /// <summary>P5.007 寫入 1000 = 停止目前運動</summary>
    public const ushort PrTrigger_Stop = 1000;

    // ============================
    //  DI 功能碼（P4.007 各 bit 對應的 DI 預設功能）
    // ============================

    /// <summary>DI 功能: 不作用</summary>
    public const ushort DI_Func_None = 0x00;

    /// <summary>DI 功能: 伺服啟動 (Servo On)</summary>
    public const ushort DI_Func_ServoOn = 0x01;

    /// <summary>DI 功能: 異常清除</summary>
    public const ushort DI_Func_AlarmClear = 0x02;

    /// <summary>DI 功能: 脈波清除</summary>
    public const ushort DI_Func_PulseClear = 0x04;

    /// <summary>DI 功能: 內部暫存器扭矩命令選擇 Bit0</summary>
    public const ushort DI_Func_TorqueSel_Bit0 = 0x16;

    /// <summary>DI 功能: 內部暫存器扭矩命令選擇 Bit1</summary>
    public const ushort DI_Func_TorqueSel_Bit1 = 0x17;

    /// <summary>DI 功能: 反轉禁止極限 (B contact)</summary>
    public const ushort DI_Func_NegLimit = 0x22;

    /// <summary>DI 功能: 正轉禁止極限 (B contact)</summary>
    public const ushort DI_Func_PosLimit = 0x23;

    /// <summary>DI 功能: 復歸原點 (B contact)</summary>
    public const ushort DI_Func_Home = 0x24;

    // ============================
    //  P3.006 / P4.007 DI 位元遮罩
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
    //  DO 功能碼（P4.006 各 bit 對應的 DO code）
    // ============================

    public const ushort DO_Code_0x30 = 0x30;  // bit00
    public const ushort DO_Code_0x31 = 0x31;  // bit01
    public const ushort DO_Code_0x32 = 0x32;  // bit02
    public const ushort DO_Code_0x33 = 0x33;  // bit03
    public const ushort DO_Code_0x34 = 0x34;  // bit04
    public const ushort DO_Code_0x35 = 0x35;  // bit05
    public const ushort DO_Code_0x36 = 0x36;  // bit06
    public const ushort DO_Code_0x37 = 0x37;  // bit07
    public const ushort DO_Code_0x38 = 0x38;  // bit08
    public const ushort DO_Code_0x39 = 0x39;  // bit09
    public const ushort DO_Code_0x3A = 0x3A;  // bit10
    public const ushort DO_Code_0x3B = 0x3B;  // bit11
    public const ushort DO_Code_0x3C = 0x3C;  // bit12
    public const ushort DO_Code_0x3D = 0x3D;  // bit13
    public const ushort DO_Code_0x3E = 0x3E;  // bit14
    public const ushort DO_Code_0x3F = 0x3F;  // bit15

    // P4.006 DO 位元遮罩
    public const ushort DO_Bit00 = 0x0001;
    public const ushort DO_Bit01 = 0x0002;
    public const ushort DO_Bit02 = 0x0004;
    public const ushort DO_Bit03 = 0x0008;
    public const ushort DO_Bit04 = 0x0010;
    public const ushort DO_Bit05 = 0x0020;
    public const ushort DO_Bit06 = 0x0040;
    public const ushort DO_Bit07 = 0x0080;
    public const ushort DO_Bit08 = 0x0100;
    public const ushort DO_Bit09 = 0x0200;
    public const ushort DO_Bit10 = 0x0400;
    public const ushort DO_Bit11 = 0x0800;
    public const ushort DO_Bit12 = 0x1000;
    public const ushort DO_Bit13 = 0x2000;
    public const ushort DO_Bit14 = 0x4000;
    public const ushort DO_Bit15 = 0x8000;
}