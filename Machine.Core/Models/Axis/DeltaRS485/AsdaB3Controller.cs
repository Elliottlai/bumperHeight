using System.Diagnostics;

namespace Machine.Core.Models.Axis.DeltaRS485
{
    /// <summary>
    /// Delta ASDA-B3 伺服驅動控制器
    /// 操作 Servo 開關、Absolute 編碼器管理、PR mode 定位
    /// </summary>
    public class AsdaB3Controller : IDisposable
    {
        private readonly IModbusRtuClient _modbus;
        private readonly object _modbusLock = new();
        private bool _disposed;

        /// <summary>目標速度 (rpm)</summary>
        public int TargetSpeed { get; set; } = 1000;

        /// <summary>加速時間 (ms)</summary>
        public int AccelerationTime { get; set; } = 200;

        /// <summary>減速時間 (ms)</summary>
        public int DecelerationTime { get; set; } = 200;

        /// <summary>延遲時間 (ms)</summary>
        public int DelayTime { get; set; }

        /// <summary>到位逾時 (ms)</summary>
        public int InPositionTimeout { get; set; } = 5000;

        /// <summary>每 mm 對應的 PUU 數量</summary>
        public double PuuPerMm { get; set; } = 1000.0;

        /// <summary>是否已激磁</summary>
        public bool IsServoOn { get; private set; }

        public AsdaB3Controller(IModbusRtuClient modbus)
        {
            _modbus = modbus ?? throw new ArgumentNullException(nameof(modbus));
        }

        /// <summary>連線後初始化驅動器</summary>
        public void Initialize()
        {
            ThrowIfDisposed();

            // 讀取版本確認通訊正常
            _modbus.ReadRegister(AsdaB3RegisterMap.P0_000_Version);

            // 設定 P2.030 = 5: 不寫入 EEPROM（通訊連續寫入時避免壽命損耗）
            _modbus.WriteRegister(AsdaB3RegisterMap.P2_030_AuxFunction, AsdaB3RegisterMap.AuxFunction_NoEepromSave);

            // 設定監視變數映射:
            // P0.009 → FeedbackPosition_PUU (回授位置)
            // P0.010 → AlarmCodeDecimal (警報碼)
            // P0.011 → DiStatusIntegrated (DI 輸出狀態)
            // P0.012 → DoStatusHardware (DO 硬體狀態)
            ConfigureMonitorMapping(
                AsdaB3MonitorCode.FeedbackPosition_PUU,
                AsdaB3MonitorCode.AlarmCodeDecimal,
                AsdaB3MonitorCode.DiStatusIntegrated,
                AsdaB3MonitorCode.DoStatusHardware);
        }

        // ============================
        //  Servo ON/OFF
        // ============================

        /// <summary>伺服激磁 (Servo ON)</summary>
        public void ServoOn()
        {
            ThrowIfDisposed();
            EnsureNoAlarm();

            // 將 DI1 (ServoOn) 和 DI5 (異常清除 ARST) 設為軟體 DI 來源
            ushort diControl = _modbus.ReadRegister(AsdaB3RegisterMap.P3_006_DiControl);
            diControl |= (ushort)(AsdaB3RegisterMap.DI1_Bit | AsdaB3RegisterMap.DI5_Bit);
            _modbus.WriteRegister(AsdaB3RegisterMap.P3_006_DiControl, diControl);

            // 透過 P4.007 設定 DI1 = ON → Servo ON
            ushort softDi = _modbus.ReadRegister(AsdaB3RegisterMap.P4_007_SoftwareDI);
            softDi |= AsdaB3RegisterMap.DI1_Bit;
            _modbus.WriteRegister(AsdaB3RegisterMap.P4_007_SoftwareDI, softDi);

            Thread.Sleep(500);
            IsServoOn = true;
        }

        /// <summary>伺服脫磁 (Servo OFF)</summary>
        public void ServoOff()
        {
            ThrowIfDisposed();

            ushort softDi = _modbus.ReadRegister(AsdaB3RegisterMap.P4_007_SoftwareDI);
            softDi &= unchecked((ushort)~AsdaB3RegisterMap.DI1_Bit);
            _modbus.WriteRegister(AsdaB3RegisterMap.P4_007_SoftwareDI, softDi);

            Thread.Sleep(200);
            IsServoOn = false;
        }

        // ============================
        //  警報
        // ============================

        /// <summary>檢查是否有警報 (P0.046 Bit6)</summary>
        public bool HasAlarm()
        {
            ThrowIfDisposed();
            return ReadDriverStatus().HasFlag(DriverStatusFlags.Alarm);
        }

        /// <summary>清除警報 (DI5 上升緣觸發)</summary>
        public void ClearAlarm()
        {
            ThrowIfDisposed();
            ushort softDi = _modbus.ReadRegister(AsdaB3RegisterMap.P4_007_SoftwareDI);

            // 拉高 DI5
            softDi |= AsdaB3RegisterMap.DI5_Bit;
            _modbus.WriteRegister(AsdaB3RegisterMap.P4_007_SoftwareDI, softDi);
            Thread.Sleep(100);

            // 放開 DI5
            softDi &= unchecked((ushort)~AsdaB3RegisterMap.DI5_Bit);
            _modbus.WriteRegister(AsdaB3RegisterMap.P4_007_SoftwareDI, softDi);
        }

        // ============================
        //  位置讀取
        // ============================

        /// <summary>讀取驅動器狀態 (P0.046)</summary>
        public DriverStatusFlags ReadDriverStatus()
        {
            ThrowIfDisposed();
            return (DriverStatusFlags)_modbus.ReadRegister(AsdaB3RegisterMap.P0_046_DriverStatus);
        }

        /// <summary>讀取回授位置 PUU (P0.051, 32-bit 多圈絕對位置)</summary>
        public int GetFeedbackPositionPuu()
        {
            ThrowIfDisposed();
            return _modbus.ReadRegister32(AsdaB3RegisterMap.P0_051_MultiTurnPos_Low);
        }

        /// <summary>讀取目前位置 (mm)</summary>
        public double GetCurrentPositionMm() => GetFeedbackPositionPuu() / PuuPerMm;

        /// <summary>將 mm 轉算成 PUU</summary>
        public int MmToPuu(double mm) => (int)(mm * PuuPerMm);

        // ============================
        //  PR Mode 定位
        // ============================

        /// <summary>更新 PR#1 的目標位置 (32-bit PUU)</summary>
        public void UpdatePr1TargetPosition(int puu)
        {
            ThrowIfDisposed();
            _modbus.WriteRegister32(AsdaB3RegisterMap.P6_003_Pr1TargetPos_Low, puu);
        }

        /// <summary>觸發 PR#1 運行 (寫 P5.007 = 1)</summary>
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

        /// <summary>等待到位完成（輪詢 P0.046 InPosition 位元，含逾時及警報檢查）</summary>
        public void WaitInPosition()
        {
            ThrowIfDisposed();
            WaitForCondition(
                () =>
                {
                    var status = ReadDriverStatus();
                    if (status.HasFlag(DriverStatusFlags.Alarm))
                        throw new ServoAlarmException(0, "等待到位期間發生警報 (P0.046 Alarm bit)");
                    return status.HasFlag(DriverStatusFlags.TargetPositionReached);
                },
                InPositionTimeout,
                $"到位逾時（超過 {InPositionTimeout} ms），請檢查機構或參數");
        }

        /// <summary>
        /// 移動到指定位置 (mm)
        /// 流程：mm → PUU → 寫入 PR#1 → 觸發 → 等待到位
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
        //  運動參數寫入
        // ============================

        /// <summary>將運動參數寫入驅動器（含 speed、acc/dec、delay、PR#1 path definition）</summary>
        public void ApplySettings()
        {
            ThrowIfDisposed();

            // 寫入 speed #1 (P5.060, 32-bit)
            _modbus.WriteRegister32(AsdaB3RegisterMap.P5_060_TargetSpeed0, TargetSpeed);

            // 寫入 acceleration/deceleration time #1 (P5.020)
            _modbus.WriteRegister(AsdaB3RegisterMap.P5_020_AccDecTime0, (ushort)AccelerationTime);

            // 寫入 delay time #1 (P5.040)
            _modbus.WriteRegister(AsdaB3RegisterMap.P5_040_DelayTime0, (ushort)DelayTime);

            // 設定 PR#1 path definition (P6.002, 32-bit) — 絕對定位模式, speed #1, acc/dec #1
            int pathDef = 0x0000_0002;
            _modbus.WriteRegister32(AsdaB3RegisterMap.P6_002_Pr1PathDef_Low, pathDef);
        }

        // ============================
        //  Absolute 編碼器
        // ============================

        /// <summary>讀取 P0.050 完整狀態旗標</summary>
        public AbsoluteStatusFlags ReadAbsoluteStatus()
        {
            ThrowIfDisposed();
            return (AbsoluteStatusFlags)_modbus.ReadRegister(AsdaB3RegisterMap.P0_050_AbsStatus);
        }

        /// <summary>檢查 absolute 座標是否完全正常 (P0.050 = 0)</summary>
        public bool AbsOk() => ReadAbsoluteStatus() == AbsoluteStatusFlags.None;

        /// <summary>
        /// 重建 absolute origin
        /// 當 absolute 座標遺失時依序：
        /// 1. 寫 P2.008 = 271 解除寫保護
        /// 2. 寫 P2.071 = 1 建立 absolute origin
        /// </summary>
        public void RebuildAbsoluteOrigin()
        {
            ThrowIfDisposed();
            _modbus.WriteRegister(AsdaB3RegisterMap.P2_008_WriteProtect, AsdaB3RegisterMap.WriteProtectUnlock);
            Thread.Sleep(100);
            _modbus.WriteRegister(AsdaB3RegisterMap.P2_071_BuildAbsOrigin, 1);
            Thread.Sleep(500);
            if (!AbsOk())
                throw new InvalidOperationException("重建 absolute origin 失敗，P0.050 顯示異常狀態");
        }

        // ============================
        //  極限 / 原點 / 煞車
        // ============================

        /// <summary>讀取 DI 訊號狀態 (P0.045)</summary>
        public ushort ReadDiStatus()
        {
            ThrowIfDisposed();
            return _modbus.ReadRegister(AsdaB3RegisterMap.P0_045_DiStatus);
        }

        /// <summary>正極限是否觸發（DI7, B contact 低電平有效）</summary>
        public bool IsPositiveLimitActive()
        {
            ThrowIfDisposed();
            return (ReadDiStatus() & AsdaB3RegisterMap.DI7_Bit) == 0;
        }

        /// <summary>負極限是否觸發（DI8, B contact 低電平有效）</summary>
        public bool IsNegativeLimitActive()
        {
            ThrowIfDisposed();
            return (ReadDiStatus() & AsdaB3RegisterMap.DI8_Bit) == 0;
        }

        /// <summary>原點復歸是否完成 (P0.046 Bit8)</summary>
        public bool IsHomeCompleted() => ReadDriverStatus().HasFlag(DriverStatusFlags.HomeComplete);

        /// <summary>是否到位 (P0.046 Bit4)</summary>
        public bool IsInPosition() => ReadDriverStatus().HasFlag(DriverStatusFlags.TargetPositionReached);

        /// <summary>伺服是否 Ready (P0.046 Bit0)</summary>
        public bool IsServoReady() => ReadDriverStatus().HasFlag(DriverStatusFlags.ServoReady);

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

            WaitForCondition(
                () =>
                {
                    var status = ReadDriverStatus();
                    if (status.HasFlag(DriverStatusFlags.Alarm))
                        throw new ServoAlarmException(0, "原點復歸期間發生警報");
                    return status.HasFlag(DriverStatusFlags.HomeComplete);
                },
                InPositionTimeout * 3,
                $"原點復歸逾時（超過 {InPositionTimeout * 3} ms）");
        }

        /// <summary>
        /// 控制電磁煞車（透過軟體 DO 控制）
        /// </summary>
        /// <param name="release">true = 釋放煞車; false = 鎖定煞車</param>
        public void SetBrake(bool release)
        {
            ThrowIfDisposed();
            ushort softDo = _modbus.ReadRegister(AsdaB3RegisterMap.P4_006_SoftwareDO);

            if (release)
                softDo |= AsdaB3RegisterMap.StatusBit_Brake;
            else
                softDo &= unchecked((ushort)~AsdaB3RegisterMap.StatusBit_Brake);

            _modbus.WriteRegister(AsdaB3RegisterMap.P4_006_SoftwareDO, softDo);
            Thread.Sleep(200);
        }

        // ============================
        //  監控映射
        // ============================

        private static readonly ushort[] MonitorSelRegisters =
        [
            AsdaB3RegisterMap.P0_017_MappedMonitorSel1,
            AsdaB3RegisterMap.P0_018_MappedMonitorSel2,
            AsdaB3RegisterMap.P0_019_MappedMonitorSel3,
            AsdaB3RegisterMap.P0_020_MappedMonitorSel4,
        ];

        /// <summary>設定監視變數映射（最多 4 組）</summary>
        public void ConfigureMonitorMapping(params ushort[] monitorCodes)
        {
            ThrowIfDisposed();
            if (monitorCodes.Length > MonitorSelRegisters.Length)
                throw new ArgumentException($"最多僅支援 {MonitorSelRegisters.Length} 組映射監視變數", nameof(monitorCodes));

            for (int i = 0; i < monitorCodes.Length; i++)
                _modbus.WriteRegister(MonitorSelRegisters[i], monitorCodes[i]);
        }

        // ============================
        //  批次快照讀取
        // ============================

        /// <summary>
        /// 批次讀取驅動器即時狀態快照
        /// 一次 Modbus 讀取 P0.046 + P0.050~P0.054 + P0.009~P0.012，共 3 次通訊
        /// </summary>
        public DriverSnapshot ReadSnapshot()
        {
            ThrowIfDisposed();

            ushort doStatus = _modbus.ReadRegister(AsdaB3RegisterMap.P0_046_DriverStatus);
            ushort[] batch = _modbus.ReadRegisters(AsdaB3RegisterMap.P0_050_AbsStatus, 10);
            ushort[] monBatch = _modbus.ReadRegisters(AsdaB3RegisterMap.P0_009_MappedMonitorValue1, 8);

            int feedbackPos = (int)(monBatch[0] | (monBatch[1] << 16));
            int alarmCode = (int)(monBatch[2] | (monBatch[3] << 16));
            int diIntegrated = (int)(monBatch[4] | (monBatch[5] << 16));
            int doHardware = (int)(monBatch[6] | (monBatch[7] << 16));

            return new DriverSnapshot
            {
                DiStatus = 0,
                DriverStatus = (DriverStatusFlags)doStatus,
                AbsoluteStatus = (AbsoluteStatusFlags)batch[0],
                MultiTurnPosition = batch[2] | (batch[3] << 16),
                SingleTurnPosition = batch[4] | (batch[5] << 16),
                FeedbackPositionPuu = feedbackPos,
                AlarmCodeDecimal = (ushort)alarmCode,
                DiStatusIntegrated = (ushort)diIntegrated,
                DoStatusHardware = (ushort)doHardware,
            };
        }

        /// <summary>用 lock 保護的 Modbus 操作（供外部呼叫時與 polling 互斥）</summary>
        public void ExecuteWithLock(Action action)
        {
            lock (_modbusLock) { action(); }
        }

        /// <summary>用 lock 保護的 Modbus 操作（供外部呼叫時與 polling 互斥）</summary>
        public T ExecuteWithLock<T>(Func<T> func)
        {
            lock (_modbusLock) { return func(); }
        }

        // ============================
        //  內部輔助
        // ============================

        private void EnsureNoAlarm()
        {
            if (HasAlarm())
            {
                var alarmCode = _modbus.ReadRegister(AsdaB3RegisterMap.AlarmCode);
                throw new ServoAlarmException(alarmCode, $"驅動器存在警報: 0x{alarmCode:X4}，請先清除警報");
            }
        }

        private static void WaitForCondition(Func<bool> condition, int timeoutMs, string errorMessage)
        {
            var sw = Stopwatch.StartNew();
            while (!condition())
            {
                if (sw.ElapsedMilliseconds > timeoutMs)
                    throw new TimeoutException(errorMessage);
                Thread.Sleep(10);
            }
        }

        private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

        /// <summary>釋放資源</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                if (IsServoOn)
                {
                    ushort softDi = _modbus.ReadRegister(AsdaB3RegisterMap.P4_007_SoftwareDI);
                    softDi &= unchecked((ushort)~AsdaB3RegisterMap.DI1_Bit);
                    _modbus.WriteRegister(AsdaB3RegisterMap.P4_007_SoftwareDI, softDi);
                    IsServoOn = false;
                }
            }
            catch { /* 忽略通訊層斷線錯誤 */ }

            _modbus.Disconnect();
            GC.SuppressFinalize(this);
        }
    }
}
