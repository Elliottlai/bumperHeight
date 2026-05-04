using Machine.Core.Enums;
using Machine.Core.Interfaces;
using Machine.Core.Models.Axis.DeltaRS485;

namespace Machine.Core
{
    /// <summary>
    /// Delta ASDA-B3 RS485 軸控制器 — 實作 IAxis 介面
    /// 類別名稱 cAxis_RS485 對應 AxisCardType.RS485 = 3，
    /// 供 cMachineManager.LoadAxises() 反射載入時自動配對。
    /// </summary>
    public class cAxis_RS485 : IAxis, IDisposable
    {
        private readonly ModbusRtuClient _modbus;
        private readonly AsdaB3Controller _controller;
        private bool _isConnected;
        private double _targetPosition;

        // ============================
        //  IComponent
        // ============================
        public string UID { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;

        // ============================
        //  IAxisArgs
        // ============================
        public AxisCardType Type => AxisCardType.RS485;
        public int AxisID { get; set; }
        public int HomeMode { get; set; }
        public double HomeSpeed { get; set; }
        public double HomeStartSpeed { get; set; }
        public double HomeAcc { get; set; }
        public double HomeDec { get; set; }
        public double HomeBuffer { get; set; }
        public double OperationSpeed { get; set; }
        public double OperationStartSpeed { get; set; }
        public double OperationAcc { get; set; }
        public double OperationDec { get; set; }
        public double Scale { get; set; } = 0.001; // mm/pulse (= 1/PuuPerMm)
        public double Tolerance { get; set; } = 0.01;
        public CurveType Curve { get; set; } = CurveType.T_Curve;
        public double SoftwareNLimit { get; set; }
        public double SoftwarePLimit { get; set; }

        // ============================
        //  RS485 連線設定
        // ============================

        /// <summary>COM Port 名稱，例如 "COM12"</summary>
        public string PortName { get; set; } = "COM3";

        /// <summary>鮑率，預設 38400</summary>
        public int BaudRate { get; set; } = 38400;

        /// <summary>Modbus 站號，預設 1</summary>
        public byte SlaveId { get; set; } = 1;

        /// <summary>PUU/mm 轉換比（對應 Scale 的倒數）</summary>
        public double PuuPerMm
        {
            get => _controller.PuuPerMm;
            set => _controller.PuuPerMm = value;
        }

        /// <summary>到位逾時 (ms)</summary>
        public int InPositionTimeout
        {
            get => _controller.InPositionTimeout;
            set => _controller.InPositionTimeout = value;
        }

        // ============================
        //  主程式新增功能：絕對編碼器 & 煞車
        // ============================

        /// <summary>絕對座標是否正常</summary>
        public bool AbsOk => _controller.AbsOk();

        /// <summary>讀取絕對編碼器狀態旗標</summary>
        public AbsoluteStatusFlags AbsoluteStatus => _controller.ReadAbsoluteStatus();

        /// <summary>重建絕對原點</summary>
        public void RebuildAbsoluteOrigin() => _controller.RebuildAbsoluteOrigin();

        /// <summary>煞車控制 (true=釋放, false=鎖定)</summary>
        public void SetBrake(bool release) => _controller.SetBrake(release);

        /// <summary>批次讀取驅動器快照</summary>
        public DriverSnapshot ReadSnapshot() => _controller.ReadSnapshot();

        /// <summary>驅動器狀態旗標</summary>
        public DriverStatusFlags DriverStatus => _controller.ReadDriverStatus();

        /// <summary>取得底層控制器（進階用途）</summary>
        public AsdaB3Controller Controller => _controller;

        public cAxis_RS485()
        {
            _modbus = new ModbusRtuClient();
            _controller = new AsdaB3Controller(_modbus);
        }

        // ============================
        //  連線 / 斷線（主程式呼叫）
        // ============================

        /// <summary>建立 RS485 連線並初始化驅動器</summary>
        public void Connect()
        {
            _modbus.Connect(PortName, BaudRate, SlaveId);
            _controller.PuuPerMm = Scale > 0 ? (1.0 / Scale) : 1000.0;
            _controller.Initialize();
            _isConnected = true;
        }

        /// <summary>斷線</summary>
        public void Disconnect()
        {
            _controller.Dispose();
            _isConnected = false;
        }

        // ============================
        //  IAxis 實作
        // ============================

        public void MotStop(bool isImmediate = false)
        {
            _controller.Stop();
        }

        public bool MotMoveAbs(double Pos)
        {
            _targetPosition = Pos;
            int puu = _controller.MmToPuu(Pos);
            _controller.UpdatePr1TargetPosition(puu);
            _controller.TriggerPr1();
            return true;
        }

        public bool MotMoveRel(double Pos)
        {
            double currentMm = _controller.GetCurrentPositionMm();
            return MotMoveAbs(currentMm + Pos);
        }

        public bool Wait()
        {
            var status = _controller.ReadDriverStatus();
            bool inPos = status.HasFlag(DriverStatusFlags.TargetPositionReached);
            bool zeroSpd = status.HasFlag(DriverStatusFlags.ZeroSpeed);
            return inPos && zeroSpd;
        }

        public double GetRealPosition()
        {
            return _controller.GetCurrentPositionMm();
        }

        public double GetLogicPosition()
        {
            return _controller.GetCurrentPositionMm();
        }

        public bool Home()
        {
            _controller.ExecuteHoming();
            return true;
        }

        public void SetSVON(bool OnorOff)
        {
            if (OnorOff)
                _controller.ServoOn();
            else
                _controller.ServoOff();
        }

        public void SetDO(int ID, bool OnorOff)
        {
            ushort softDo = _modbus.ReadRegister(AsdaB3RegisterMap.P4_006_SoftwareDO);
            ushort bit = (ushort)(1 << ID);
            if (OnorOff)
                softDo |= bit;
            else
                softDo &= unchecked((ushort)~bit);
            _modbus.WriteRegister(AsdaB3RegisterMap.P4_006_SoftwareDO, softDo);
        }

        public void SetMaxVel(double Value)
        {
            _controller.TargetSpeed = (int)Value;
            _controller.ApplySettings();
        }

        public void SetStrVel(double Value)
        {
            // Delta PR mode 不支援獨立啟動速度，忽略
        }

        public void SetAccTime(double Value)
        {
            _controller.AccelerationTime = (int)(Value * 1000); // 秒 → ms
            _controller.ApplySettings();
        }

        public void SetDecTime(double Value)
        {
            _controller.DecelerationTime = (int)(Value * 1000); // 秒 → ms
            _controller.ApplySettings();
        }

        public void SetCurve(CurveType Curve)
        {
            this.Curve = Curve;
            // Delta ASDA-B3 PR mode 不支援切換加減速曲線型式
        }

        public void SetTrigger(double Position, int CompareMethods)
        {
            // Delta RS485 不支援硬體 trigger/compare
        }

        public void ResetError()
        {
            _controller.ClearAlarm();
        }

        public bool GetIOStatus(int ID)
        {
            ushort diStatus = _controller.ReadDiStatus();
            return (diStatus & (1 << ID)) != 0;
        }

        public bool GetPLimit() => _controller.IsPositiveLimitActive();

        public bool GetNLimit() => _controller.IsNegativeLimitActive();

        public bool GetOrg() => _controller.IsHomeCompleted();

        public bool GetSVON() => _controller.IsServoOn;

        public bool GetINP() => _controller.IsInPosition();

        public bool GetRDY() => _controller.IsServoReady();

        public bool GetAlarm() => _controller.HasAlarm();

        public bool GetTrigger()
        {
            return false; // Delta RS485 不支援硬體 trigger
        }

        public bool GetEmergency()
        {
            var status = _controller.ReadDriverStatus();
            return status.HasFlag(DriverStatusFlags.Warning);
        }

        public void SetPosition(double Pos)
        {
            // Delta 絕對編碼器模式下通常不需要手動設定位置
            // 如需重設原點可呼叫 RebuildAbsoluteOrigin()
        }

        public void MotPrevious()
        {
            MotMoveAbs(_targetPosition);
        }

        public double GetTargetPosition() => _targetPosition;

        // ============================
        //  Dispose
        // ============================

        public void Dispose()
        {
            _controller?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
