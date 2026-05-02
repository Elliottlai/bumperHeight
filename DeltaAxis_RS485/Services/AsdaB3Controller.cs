using DeltaAxis_RS485.Helper;
using DeltaAxis_RS485.Interfaces;
using DeltaAxis_RS485.Models;
using System.IO;

namespace DeltaAxis_RS485.Services;

/// <summary>
/// Delta ASDA-B3 伺服驅動器控制器
/// 實作 Servo 控制、Absolute 編碼器管理、PR mode 定位
/// </summary>
public class AsdaB3Controller : IServoDriver, IAbsoluteEncoder, IPrMotionController, IPrMotionSettings, IDisposable
{
    private readonly IModbusRtuClient _modbus;
    private readonly ConnectionSettings _connSettings;
    private bool _disposed;

    // ============================
    //  IPrMotionSettings 屬性
    // ============================

    /// <summary>目標速度 (rpm)</summary>
    public int TargetSpeed { get; set; } = 1000;

    /// <summary>加速時間 (ms)</summary>
    public int AccelerationTime { get; set; } = 200;

    /// <summary>減速時間 (ms)</summary>
    public int DecelerationTime { get; set; } = 200;

    /// <summary>延遲時間 (ms)</summary>
    public int DelayTime { get; set; } = 0;

    /// <summary>到位逾時 (ms)</summary>
    public int InPositionTimeout { get; set; } = 5000;

    /// <summary>每 mm 對應的 PUU 數量</summary>
    public double PuuPerMm { get; set; } = 1000.0;

    /// <summary>是否已激磁</summary>
    public bool IsServoOn { get; private set; }

    /// <summary>
    /// 建構函式
    /// </summary>
    /// <param name="modbus">Modbus RTU 通訊實例</param>
    /// <param name="connSettings">連線設定</param>
    /// <param name="motionSettings">運動參數（可選）</param>
    public AsdaB3Controller(IModbusRtuClient modbus, ConnectionSettings connSettings, MotionSettings? motionSettings = null)
    {
        _modbus = modbus ?? throw new ArgumentNullException(nameof(modbus));
        _connSettings = connSettings ?? throw new ArgumentNullException(nameof(connSettings));

        if (motionSettings != null)
        {
            TargetSpeed = motionSettings.TargetSpeed;
            AccelerationTime = motionSettings.AccelerationTime;
            DecelerationTime = motionSettings.DecelerationTime;
            DelayTime = motionSettings.DelayTime;
            InPositionTimeout = motionSettings.InPositionTimeout;
            PuuPerMm = motionSettings.PuuPerMm;
        }
    }

    // ============================
    //  IServoDriver 實作
    // ============================

    /// <summary>建立通訊連線並初始化驅動器</summary>
    public void Connect()
    {
        ThrowIfDisposed();

        try
        {
            _modbus.Connect(_connSettings.PortName, _connSettings.BaudRate, _connSettings.SlaveId);

            var a = _modbus.ReadRegister(AsdaB3RegisterMap.P0_000_Version);
        }
        catch (Exception ex) when (ex is not ObjectDisposedException)
        {
            throw new InvalidOperationException(
                $"無法連線至 {_connSettings.PortName} (BaudRate={_connSettings.BaudRate}, SlaveId={_connSettings.SlaveId})", ex);
        }

        // 設定 P2.030 = 5: 防止 EEPROM 因通訊連續寫入而減短壽命
        _modbus.WriteRegister(AsdaB3RegisterMap.P2_030_AuxFunction, AsdaB3RegisterMap.AuxFunction_NoEepromSave);

        // 設定控制模式為 PR mode
        _modbus.WriteRegister(AsdaB3RegisterMap.P1_001_ControlMode, AsdaB3RegisterMap.ControlMode_PR);

        // 設定監視變數映射:
        // P0.009 → FeedbackPosition_PUU (回授位置)
        // P0.010 → AlarmCodeDecimal (警報碼)
        // P0.011 → DiStatusIntegrated (DI 整合狀態)
        // P0.012 → DoStatusHardware (DO 硬體狀態)
        ConfigureMonitorMapping(
            AsdaB3MonitorCode.FeedbackPosition_PUU,
            AsdaB3MonitorCode.AlarmCodeDecimal,
            AsdaB3MonitorCode.DiStatusIntegrated,
            AsdaB3MonitorCode.DoStatusHardware);
    }

    /// <summary>伺服激磁 (Servo ON)</summary>
    public void ServoOn()
    {
        ThrowIfDisposed();
        EnsureNoAlarm();

        // 步驟 1: 將 DI1 (ServoOn) 切換為軟體 DI 來源
        //   P3.006 Bit0 = 1 → DI1 由 P4.007 控制
        ushort diControl = _modbus.ReadRegister(AsdaB3RegisterMap.P3_006_DiControl);
        diControl |= AsdaB3RegisterMap.DI1_Bit;
        _modbus.WriteRegister(AsdaB3RegisterMap.P3_006_DiControl, diControl);

        // 步驟 2: 將 DI5 (異常清除 ARST) 也切為軟體 DI
        //   P3.006 Bit4 = 1 → DI5 由 P4.007 控制
        diControl |= AsdaB3RegisterMap.DI5_Bit;
        _modbus.WriteRegister(AsdaB3RegisterMap.P3_006_DiControl, diControl);

        // 步驟 3: 透過 P4.007 設定 DI1 = ON (Bit0 = 1) → Servo ON
        ushort softDi = _modbus.ReadRegister(AsdaB3RegisterMap.P4_007_SoftwareDI);
        softDi |= AsdaB3RegisterMap.DI1_Bit;  // DI1 = Servo On
        _modbus.WriteRegister(AsdaB3RegisterMap.P4_007_SoftwareDI, softDi);

        // 等待激磁完成（給驅動器反應時間）
        Thread.Sleep(500);

        IsServoOn = true;
    }

    /// <summary>伺服解磁 (Servo OFF)</summary>
    public void ServoOff()
    {
        ThrowIfDisposed();

        // 透過 P4.007 清除 DI1 = OFF (Bit0 = 0) → Servo OFF
        ushort softDi = _modbus.ReadRegister(AsdaB3RegisterMap.P4_007_SoftwareDI);
        softDi &= unchecked((ushort)~AsdaB3RegisterMap.DI1_Bit);
        _modbus.WriteRegister(AsdaB3RegisterMap.P4_007_SoftwareDI, softDi);

        Thread.Sleep(200);
        IsServoOn = false;
    }

    /// <summary>檢查是否有警報（透過 P0.046 Bit6 判斷）</summary>
    public bool HasAlarm()
    {
        ThrowIfDisposed();

        var status = ReadDriverStatus();
        return status.HasFlag(DriverStatusFlags.Alarm);
    }

    /// <summary>檢查伺服是否 Ready（透過 P0.046 Bit0 判斷）</summary>
    public bool IsServoReady()
    {
        ThrowIfDisposed();

        var status = ReadDriverStatus();
        return status.HasFlag(DriverStatusFlags.ServoReady);
    }

    /// <summary>檢查是否到位（透過 P0.046 Bit4 判斷）</summary>
    public bool IsInPosition()
    {
        ThrowIfDisposed();

        var status = ReadDriverStatus();
        return status.HasFlag(DriverStatusFlags.TargetPositionReached);
    }

    // ============================
    //  IAbsoluteEncoder 實作
    // ============================

    /// <summary>檢查 absolute 座標是否全部正常 (P0.050 = 0 表示正常)</summary>
    public bool AbsOk()
    {
        ThrowIfDisposed();

        return ReadAbsoluteStatus() == AbsoluteStatusFlags.None;
    }

    /// <summary>讀取 P0.050 完整狀態旗標</summary>
    public AbsoluteStatusFlags ReadAbsoluteStatus()
    {
        ThrowIfDisposed();

        ushort raw = _modbus.ReadRegister(AsdaB3RegisterMap.P0_050_AbsStatus);
        return (AbsoluteStatusFlags)raw;
    }

    /// <summary>檢查絕對位置是否正常 (P0.050 Bit0 = 0 為正常)</summary>
    public bool IsAbsolutePositionOk()
    {
        ThrowIfDisposed();

        var status = ReadAbsoluteStatus();
        return !status.HasFlag(AbsoluteStatusFlags.AbsolutePosition);
    }

    /// <summary>檢查電池電壓是否正常 (P0.050 Bit1 = 0 為正常)</summary>
    public bool IsBatteryVoltageOk()
    {
        ThrowIfDisposed();

        var status = ReadAbsoluteStatus();
        return !status.HasFlag(AbsoluteStatusFlags.BatteryVoltage);
    }

    /// <summary>檢查絕對圈數是否正常 (P0.050 Bit2 = 0 為正常)</summary>
    public bool IsAbsoluteRevolutionOk()
    {
        ThrowIfDisposed();

        var status = ReadAbsoluteStatus();
        return !status.HasFlag(AbsoluteStatusFlags.AbsoluteRevolution);
    }

    /// <summary>檢查 PUU 狀態是否正常 (P0.050 Bit3 = 0 為正常)</summary>
    public bool IsPuuStatusOk()
    {
        ThrowIfDisposed();

        var status = ReadAbsoluteStatus();
        return !status.HasFlag(AbsoluteStatusFlags.Puu);
    }

    /// <summary>檢查絕對座標是否正常 (P0.050 Bit4 = 0 為正常)</summary>
    public bool IsAbsoluteCoordinateOk()
    {
        ThrowIfDisposed();

        var status = ReadAbsoluteStatus();
        return !status.HasFlag(AbsoluteStatusFlags.AbsoluteCoordinate);
    }

    /// <summary>
    /// 重建 absolute origin
    /// 當 absolute 座標遺失時，依序：
    /// 1. 寫 P2.008 = 271 解除寫保護
    /// 2. 寫 P2.071 = 1 建立 absolute origin
    /// </summary>
    public void RebuildAbsoluteOrigin()
    {
        ThrowIfDisposed();

        // 步驟 1: 解除寫保護
        _modbus.WriteRegister(AsdaB3RegisterMap.P2_008_WriteProtect, AsdaB3RegisterMap.WriteProtectUnlock);
        Thread.Sleep(100);

        // 步驟 2: 建立 absolute origin
        _modbus.WriteRegister(AsdaB3RegisterMap.P2_071_BuildAbsOrigin, 1);
        Thread.Sleep(500);

        // 驗證是否建立成功
        if (!AbsOk())
        {
            throw new InvalidOperationException("重建 absolute origin 失敗，P0.050 仍為異常狀態");
        }
    }

    /// <summary>讀取目前多圈位置 (P0.051, 32-bit)</summary>
    public int GetMultiTurnPosition()
    {
        ThrowIfDisposed();

        return _modbus.ReadRegister32(AsdaB3RegisterMap.P0_051_MultiTurnPos_Low);
    }

    /// <summary>讀取目前單圈位置 (P0.052, 32-bit)</summary>
    public int GetSingleTurnPosition()
    {
        ThrowIfDisposed();

        return _modbus.ReadRegister32(AsdaB3RegisterMap.P0_052_SingleTurnPos_Low);
    }

    /// <summary>讀取驅動器狀態 (P0.046)</summary>
    public DriverStatusFlags ReadDriverStatus()
    {
        ThrowIfDisposed();
        ushort raw = _modbus.ReadRegister(AsdaB3RegisterMap.P0_046_DriverStatus);
        return (DriverStatusFlags)raw;
    }

    // ============================
    //  IPrMotionController 實作
    // ============================

    /// <summary>將 mm 換算成 PUU</summary>
    public int MmToPuu(double mm)
    {
        return (int)(mm * PuuPerMm);
    }

    /// <summary>讀取目前位置 (mm)，透過多圈絕對位置 P0.051 換算</summary>
    public double GetCurrentPositionMm()
    {
        ThrowIfDisposed();

        int puu = GetMultiTurnPosition();
        return puu / PuuPerMm;
    }

    /// <summary>更新 PR#1 的目標位置 (32-bit PUU，使用 FC16 一次寫入 Low/High)</summary>
    public void UpdatePr1TargetPosition(int puu)
    {
        ThrowIfDisposed();

        _modbus.WriteRegister32(AsdaB3RegisterMap.P6_003_Pr1TargetPos_Low, puu);
    }

    /// <summary>觸發 PR#1 執行 (寫 P5.007 = 1)</summary>
    public void TriggerPr1()
    {
        ThrowIfDisposed();
        EnsureNoAlarm();

        _modbus.WriteRegister(AsdaB3RegisterMap.P5_007_PrTrigger, AsdaB3RegisterMap.PrNumber_1);
    }

    /// <summary>停止目前運動 (寫 P5.007 = 1000)</summary>
    public void Stop()
    {
        ThrowIfDisposed();
        _modbus.WriteRegister(AsdaB3RegisterMap.P5_007_PrTrigger, AsdaB3RegisterMap.PrTrigger_Stop);
    }

    /// <summary>等待到位完成（輪詢 P0.046 InPosition 位元，含逾時與警報檢查）</summary>
    public void WaitInPosition()
    {
        ThrowIfDisposed();

        WaitForCondition(
            () =>
            {
                var status = ReadDriverStatus();

                // P0.046 Bit6: 警報
                if (status.HasFlag(DriverStatusFlags.Alarm))
                {
                    throw new ServoAlarmException(0, "等待到位期間發生警報 (P0.046 Alarm bit)");
                }

                // P0.046 Bit4: 目標位置到達
                return status.HasFlag(DriverStatusFlags.TargetPositionReached);
            },
            timeoutMs: InPositionTimeout,
            errorMessage: $"到位逾時（超過 {InPositionTimeout} ms），請檢查機構或參數"
        );
    }

    /// <summary>
    /// 移動到指定位置 (mm)
    /// 整合：mm → PUU → 寫入 PR#1 → 觸發 → 等待到位
    /// </summary>
    public void MoveToPositionMm(double mm)
    {
        ThrowIfDisposed();
        EnsureNoAlarm();

        int puu = MmToPuu(mm);
        UpdatePr1TargetPosition(puu);
        TriggerPr1();
        WaitInPosition();
    }

    // ============================
    //  IPrMotionSettings 實作
    // ============================

    /// <summary>
    /// 將運動參數寫入驅動器
    /// 包含 speed #1 (32-bit)、acc/dec #1、delay #1、PR#1 path definition (32-bit)
    /// </summary>
    public void ApplySettings()
    {
        ThrowIfDisposed();

        // 寫入 speed #1 (P5.061, 32-bit) — 使用 FC16 一次寫入
        _modbus.WriteRegister32(AsdaB3RegisterMap.P5_060_TargetSpeed0, TargetSpeed);

        // 寫入 acceleration / deceleration time #1 (P5.020)
        _modbus.WriteRegister(AsdaB3RegisterMap.P5_020_AccDecTime0, (ushort)AccelerationTime);

        // 寫入 delay time #1 (P5.040)
        _modbus.WriteRegister(AsdaB3RegisterMap.P5_040_DelayTime0, (ushort)DelayTime);

        // 設定 PR#1 path definition (P6.002, 32-bit) — 使用 FC16 一次寫入
        int pathDef = 0x0000_0002; // 絕對定位模式, 使用 speed #1, acc/dec #1
        _modbus.WriteRegister32(AsdaB3RegisterMap.P6_002_Pr1PathDef_Low, pathDef);
    }

    /// <summary>清除警報</summary>
    public void ClearAlarm()
    {
        ThrowIfDisposed();

        // 透過 P4.007 DI5 (異常清除) 做上升緣觸發
        ushort softDi = _modbus.ReadRegister(AsdaB3RegisterMap.P4_007_SoftwareDI);

        // 拉高 DI5 (Bit4)
        softDi |= AsdaB3RegisterMap.DI5_Bit;
        _modbus.WriteRegister(AsdaB3RegisterMap.P4_007_SoftwareDI, softDi);
        Thread.Sleep(100);

        // 放開 DI5
        softDi &= unchecked((ushort)~AsdaB3RegisterMap.DI5_Bit);
        _modbus.WriteRegister(AsdaB3RegisterMap.P4_007_SoftwareDI, softDi);
    }

    // ============================
    //  極限 / 原點 / 煞車 功能
    // ============================

    /// <summary>讀取 DI 訊號狀態 (P0.045)</summary>
    public ushort ReadDiStatus()
    {
        ThrowIfDisposed();
        return _modbus.ReadRegister(AsdaB3RegisterMap.P0_045_DiStatus);
    }

    /// <summary>正極限是否觸發（DI 對應的正轉禁止信號，低電位有效）</summary>
    public bool IsPositiveLimitActive()
    {
        ThrowIfDisposed();
        var status = ReadDriverStatus();
        // 透過 P0.046 Warning bit 搭配 DI 狀態判斷
        // 直接讀取 DI 狀態暫存器，正極限通常配置在 DI7 (Bit6)
        ushort diStatus = ReadDiStatus();
        return (diStatus & AsdaB3RegisterMap.DI7_Bit) == 0; // B contact: 低電位 = 觸發
    }

    /// <summary>負極限是否觸發（DI 對應的反轉禁止信號，低電位有效）</summary>
    public bool IsNegativeLimitActive()
    {
        ThrowIfDisposed();
        ushort diStatus = ReadDiStatus();
        return (diStatus & AsdaB3RegisterMap.DI8_Bit) == 0; // B contact: 低電位 = 觸發
    }

    /// <summary>原點復歸是否完成（P0.046 Bit8）</summary>
    public bool IsHomeCompleted()
    {
        ThrowIfDisposed();
        var status = ReadDriverStatus();
        return status.HasFlag(DriverStatusFlags.HomeComplete);
    }

    /// <summary>電磁煞車是否釋放（P0.046 Bit7）</summary>
    public bool IsBrakeReleased()
    {
        ThrowIfDisposed();
        var status = ReadDriverStatus();
        return status.HasFlag(DriverStatusFlags.BrakeRelease);
    }

    /// <summary>
    /// 執行原點復歸動作（觸發 PR#0 = Homing）
    /// </summary>
    public void ExecuteHoming()
    {
        ThrowIfDisposed();
        EnsureNoAlarm();

        if (!IsServoOn)
            throw new InvalidOperationException("請先激磁 (Servo ON) 再執行原點復歸");

        // 觸發 Homing: 寫 P5.007 = 0 (PR#0 為原點復歸路徑)
        _modbus.WriteRegister(AsdaB3RegisterMap.P5_007_PrTrigger, 0);

        // 等待原點復歸完成
        WaitForCondition(
            () =>
            {
                var status = ReadDriverStatus();
                if (status.HasFlag(DriverStatusFlags.Alarm))
                    throw new ServoAlarmException(0, "原點復歸期間發生警報");
                return status.HasFlag(DriverStatusFlags.HomeComplete);
            },
            timeoutMs: InPositionTimeout * 3,
            errorMessage: $"原點復歸逾時（超過 {InPositionTimeout * 3} ms）"
        );
    }

    /// <summary>
    /// 控制電磁煞車（透過軟體 DO 控制）
    /// </summary>
    /// <param name="release">true = 釋放煞車; false = 鎖住煞車</param>
    public void SetBrake(bool release)
    {
        ThrowIfDisposed();

        ushort softDo = _modbus.ReadRegister(AsdaB3RegisterMap.P4_006_SoftwareDO);

        if (release)
            softDo |= AsdaB3RegisterMap.StatusBit_Brake; // Bit4 = 1: 釋放煞車
        else
            softDo &= unchecked((ushort)~AsdaB3RegisterMap.StatusBit_Brake); // Bit4 = 0: 鎖住煞車

        _modbus.WriteRegister(AsdaB3RegisterMap.P4_006_SoftwareDO, softDo);
        Thread.Sleep(200);
    }

    // ============================
    //  內部輔助方法
    // ============================

    /// <summary>確認無警報，否則拋出 ServoAlarmException</summary>
    private void EnsureNoAlarm()
    {
        if (HasAlarm())
        {
            var alarmCode = _modbus.ReadRegister(AsdaB3RegisterMap.AlarmCode);
            throw new ServoAlarmException(alarmCode, $"驅動器存在警報: 0x{alarmCode:X4}，請先排除警報");
        }
    }

    /// <summary>等待條件成立，含逾時檢查</summary>
    private static void WaitForCondition(Func<bool> condition, int timeoutMs, string errorMessage)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            if (sw.ElapsedMilliseconds > timeoutMs)
                throw new TimeoutException(errorMessage);

            Thread.Sleep(10); // 輪詢間隔 10ms
        }
    }

    /// <summary>檢查物件是否已被釋放</summary>
    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    /// <summary>釋放資源</summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        try
        {
            if (IsServoOn)
            {
                // 透過 P4.007 清除 DI1 → Servo OFF
                ushort softDi = _modbus.ReadRegister(AsdaB3RegisterMap.P4_007_SoftwareDI);
                softDi &= unchecked((ushort)~AsdaB3RegisterMap.DI1_Bit);
                _modbus.WriteRegister(AsdaB3RegisterMap.P4_007_SoftwareDI, softDi);
                IsServoOn = false;
            }
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or InvalidOperationException)
        {
            // 僅忽略通訊相關的錯誤
        }

        _modbus.Disconnect();
        GC.SuppressFinalize(this);
    }

    // ============================
    //  監視變數映射 (通訊讀取)
    // ============================

    /// <summary>映射選擇暫存器 (P0.017~P0.020)</summary>
    private static readonly ushort[] MonitorSelRegisters =
    [
        AsdaB3RegisterMap.P0_017_MappedMonitorSel1,
        AsdaB3RegisterMap.P0_018_MappedMonitorSel2,
        AsdaB3RegisterMap.P0_019_MappedMonitorSel3,
        AsdaB3RegisterMap.P0_020_MappedMonitorSel4,
    ];

    /// <summary>映射回傳值暫存器 (P0.009~P0.012)</summary>
    private static readonly ushort[] MonitorValueRegisters =
    [
        AsdaB3RegisterMap.P0_009_MappedMonitorValue1,
        AsdaB3RegisterMap.P0_010_MappedMonitorValue2,
        AsdaB3RegisterMap.P0_011_MappedMonitorValue3,
        AsdaB3RegisterMap.P0_012_MappedMonitorValue4,
    ];

    /// <summary>
    /// 設定監視變數映射（最多 4 組）
    /// <para>將指定的監視代碼寫入 P0.017~P0.020，後續可透過 ReadMonitorValues 讀取</para>
    /// </summary>
    /// <param name="monitorCodes">監視變數代碼陣列，使用 <see cref="AsdaB3MonitorCode"/> 定義的常數</param>
    /// <exception cref="ArgumentException">超過 4 組映射</exception>
    public void ConfigureMonitorMapping(params ushort[] monitorCodes)
    {
        ThrowIfDisposed();

        if (monitorCodes.Length > MonitorSelRegisters.Length)
            throw new ArgumentException($"最多僅支援 {MonitorSelRegisters.Length} 組映射監視變數", nameof(monitorCodes));

        for (int i = 0; i < monitorCodes.Length; i++)
        {
            _modbus.WriteRegister(MonitorSelRegisters[i], monitorCodes[i]);
        }
    }

    /// <summary>
    /// 讀取已映射的監視變數值（對應 P0.009~P0.012）
    /// <para>呼叫前須先透過 <see cref="ConfigureMonitorMapping"/> 設定映射代碼</para>
    /// </summary>
    /// <param name="count">欲讀取的組數 (1~4)</param>
    /// <returns>各組映射監視變數的 16-bit 值</returns>
    public ushort[] ReadMonitorValues(int count = 4)
    {
        ThrowIfDisposed();

        if (count < 1 || count > MonitorValueRegisters.Length)
            throw new ArgumentOutOfRangeException(nameof(count), $"count 須介於 1~{MonitorValueRegisters.Length}");

        var results = new ushort[count];
        for (int i = 0; i < count; i++)
        {
            results[i] = _modbus.ReadRegister(MonitorValueRegisters[i]);
        }

        return results;
    }

    /// <summary>
    /// 讀取單一映射監視變數（32-bit，適用 Low/High word 合併的監視變數）
    /// </summary>
    /// <param name="channel">映射通道 (0~3，對應 P0.009~P0.012)</param>
    /// <returns>32-bit 監視值</returns>
    public int ReadMonitorValue32(int channel)
    {
        ThrowIfDisposed();

        if (channel < 0 || channel >= MonitorValueRegisters.Length)
            throw new ArgumentOutOfRangeException(nameof(channel), $"channel 須介於 0~{MonitorValueRegisters.Length - 1}");

        return _modbus.ReadRegister32(MonitorValueRegisters[channel]);
    }

    // ============================
    //  批次讀取 (減少通訊次數)
    // ============================

    /// <summary>
    /// 批次讀取驅動器即時狀態快照
    /// <para>一次 Modbus 讀取 P0.045~P0.046 (2 regs)，再讀 P0.050~P0.053 (4 regs)，共 2 次通訊</para>
    /// </summary>
    public DriverSnapshot ReadSnapshot()
    {
        ThrowIfDisposed();

        // P0.046 DO 輸出訊號狀態 (0x005C)
        ushort doStatus = _modbus.ReadRegister(AsdaB3RegisterMap.P0_046_DriverStatus);

        // P0.050~P0.054 (每參數佔 2 addr，共 10 registers)
        ushort[] batch = _modbus.ReadRegisters(AsdaB3RegisterMap.P0_050_AbsStatus, 10);

        // 讀取映射監視變數 P0.009~P0.012 (位址 0x0012 起，連續 8 個 16-bit = 4 個 32-bit)
        ushort[] monBatch = _modbus.ReadRegisters(AsdaB3RegisterMap.P0_009_MappedMonitorValue1, 8);

        int feedbackPos = (int)(monBatch[0] | (monBatch[1] << 16));   // P0.009 (32-bit)
        int alarmCode = (int)(monBatch[2] | (monBatch[3] << 16));     // P0.010 (32-bit)
        int diIntegrated = (int)(monBatch[4] | (monBatch[5] << 16));  // P0.011 (32-bit)
        int doHardware = (int)(monBatch[6] | (monBatch[7] << 16));    // P0.012 (32-bit)

        return new DriverSnapshot
        {
            DiStatus = 0,
            DriverStatus = (DriverStatusFlags)doStatus,
            AbsoluteStatus = (AbsoluteStatusFlags)batch[0],
            MultiTurnPosition = (int)(batch[2] | (batch[3] << 16)),
            SingleTurnPosition = (int)(batch[4] | (batch[5] << 16)),
            FeedbackPositionPuu = feedbackPos,
            AlarmCodeDecimal = (ushort)alarmCode,
            DiStatusIntegrated = (ushort)diIntegrated,
            DoStatusHardware = (ushort)doHardware,
        };
    }
}