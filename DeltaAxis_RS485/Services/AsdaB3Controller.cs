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
    public double PuuPerMm { get; set; } = 10000.0;

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
        }
        catch (Exception ex) when (ex is not ObjectDisposedException)
        {
            throw new InvalidOperationException(
                $"無法連線至 {_connSettings.PortName} (BaudRate={_connSettings.BaudRate}, SlaveId={_connSettings.SlaveId})", ex);
        }

        // 設定控制模式為 PR mode
        _modbus.WriteRegister(AsdaB3RegisterMap.P1_001_ControlMode, AsdaB3RegisterMap.ControlMode_PR);
    }

    /// <summary>伺服激磁 (Servo ON)</summary>
    public void ServoOn()
    {
        ThrowIfDisposed();
        EnsureNoAlarm();

        // 透過 DI 虛擬暫存器送出 Servo ON 指令
        _modbus.WriteRegister(AsdaB3RegisterMap.DiVirtualReg, 0x0001);

        // 等待 Servo ON 完成
        WaitForCondition(
            () =>
            {
                var status = _modbus.ReadRegister(AsdaB3RegisterMap.StatusWord);
                return (status & AsdaB3RegisterMap.StatusBit_ServoOn) != 0;
            },
            timeoutMs: 3000,
            errorMessage: "Servo ON 逾時，請檢查驅動器狀態"
        );

        IsServoOn = true;
    }

    /// <summary>伺服解磁 (Servo OFF)</summary>
    public void ServoOff()
    {
        ThrowIfDisposed();

        // 透過 DI 虛擬暫存器送出 Servo OFF 指令
        _modbus.WriteRegister(AsdaB3RegisterMap.DiVirtualReg, 0x0000);
        IsServoOn = false;
    }

    /// <summary>檢查是否有警報</summary>
    public bool HasAlarm()
    {
        ThrowIfDisposed();

        var status = _modbus.ReadRegister(AsdaB3RegisterMap.StatusWord);
        return (status & AsdaB3RegisterMap.StatusBit_Alarm) != 0;
    }

    /// <summary>清除警報</summary>
    public void ClearAlarm()
    {
        ThrowIfDisposed();

        // 通常透過 DI 虛擬暫存器的 ARST bit 清除
        _modbus.WriteRegister(AsdaB3RegisterMap.DiVirtualReg, 0x0004);
        Thread.Sleep(100);
        _modbus.WriteRegister(AsdaB3RegisterMap.DiVirtualReg, 0x0000);
    }

    // ============================
    //  IAbsoluteEncoder 實作
    // ============================

    /// <summary>檢查 absolute 座標是否正常 (P0.050 = 0 表示正常)</summary>
    public bool AbsOk()
    {
        ThrowIfDisposed();

        var status = _modbus.ReadRegister(AsdaB3RegisterMap.P0_050_AbsStatus);
        return status == 0;
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

        ushort low = _modbus.ReadRegister(AsdaB3RegisterMap.P0_051_MultiTurnPos_Low);
        ushort high = _modbus.ReadRegister(AsdaB3RegisterMap.P0_051_MultiTurnPos_High);
        return ModbusWordHelper.CombineInt32(low, high);
    }

    /// <summary>讀取目前單圈位置 (P0.052, 32-bit)</summary>
    public int GetSingleTurnPosition()
    {
        ThrowIfDisposed();

        ushort low = _modbus.ReadRegister(AsdaB3RegisterMap.P0_052_SingleTurnPos_Low);
        ushort high = _modbus.ReadRegister(AsdaB3RegisterMap.P0_052_SingleTurnPos_High);
        return ModbusWordHelper.CombineInt32(low, high);
    }

    // ============================
    //  IPrMotionController 實作
    // ============================

    /// <summary>將 mm 換算成 PUU</summary>
    public int MmToPuu(double mm)
    {
        return (int)(mm * PuuPerMm);
    }

    /// <summary>更新 PR#1 的目標位置 (32-bit PUU，成對寫入 Low/High)</summary>
    public void UpdatePr1TargetPosition(int puu)
    {
        ThrowIfDisposed();

        var (low, high) = ModbusWordHelper.SplitInt32(puu);
        _modbus.WriteSingleRegister(AsdaB3RegisterMap.P6_003_Pr1TargetPos_Low, low);
        _modbus.WriteSingleRegister(AsdaB3RegisterMap.P6_003_Pr1TargetPos_High, high);
    }

    /// <summary>觸發 PR#1 執行 (寫 P5.007 = 1)</summary>
    public void TriggerPr1()
    {
        ThrowIfDisposed();
        EnsureNoAlarm();

        _modbus.WriteRegister(AsdaB3RegisterMap.P5_007_PrTrigger, AsdaB3RegisterMap.PrNumber_1);
    }

    /// <summary>等待到位完成（輪詢 InPosition 位元，含逾時與警報檢查）</summary>
    public void WaitInPosition()
    {
        ThrowIfDisposed();

        WaitForCondition(
            () =>
            {
                // 每次輪詢都先檢查警報
                var status = _modbus.ReadRegister(AsdaB3RegisterMap.StatusWord);
                if ((status & AsdaB3RegisterMap.StatusBit_Alarm) != 0)
                {
                    var alarmCode = _modbus.ReadRegister(AsdaB3RegisterMap.AlarmCode);
                    throw new ServoAlarmException(alarmCode, $"等待到位期間發生警報，代碼: 0x{alarmCode:X4}");
                }

                return (status & AsdaB3RegisterMap.StatusBit_InPosition) != 0;
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

        // 寫入 speed #1 (P5.061, 32-bit)
        var (speedLow, speedHigh) = ModbusWordHelper.SplitInt32(TargetSpeed);
        _modbus.WriteSingleRegister(AsdaB3RegisterMap.P5_061_TargetSpeed1_Low, speedLow);
        _modbus.WriteSingleRegister(AsdaB3RegisterMap.P5_061_TargetSpeed1_High, speedHigh);

        // 寫入 acceleration / deceleration time #1 (P5.020)
        _modbus.WriteRegister(AsdaB3RegisterMap.P5_020_AccDecTime1, (ushort)AccelerationTime);

        // 寫入 delay time #1 (P5.040)
        _modbus.WriteRegister(AsdaB3RegisterMap.P5_040_DelayTime1, (ushort)DelayTime);

        // 設定 PR#1 path definition (P6.002, 32-bit): 絕對定位, speed #1, acc/dec #1
        ushort pathDefLow = 0x0001;   // placeholder: 絕對定位模式
        ushort pathDefHigh = 0x0000;  // placeholder: 使用 speed #1, acc/dec #1
        _modbus.WriteSingleRegister(AsdaB3RegisterMap.P6_002_Pr1PathDef_Low, pathDefLow);
        _modbus.WriteSingleRegister(AsdaB3RegisterMap.P6_002_Pr1PathDef_High, pathDefHigh);
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
                _modbus.WriteRegister(AsdaB3RegisterMap.DiVirtualReg, 0x0000);
                IsServoOn = false;
            }
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or InvalidOperationException)
        {
            // 僅忽略通訊相關的錯誤，其他異常仍會傳播
        }

        _modbus.Disconnect();
        GC.SuppressFinalize(this);
    }
}