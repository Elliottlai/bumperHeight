using Machine.Core.Enums;
using Machine.Core.Interfaces;
using Machine.Core.Models.Axis.DeltaRS485;
using NLog;
using System.Diagnostics;

namespace Machine.Core
{
    /// <summary>
    /// Delta ASDA-B3 RS485 �b��� �X ��@ IAxis ����
    /// ���O�W�� cAxis_RS485 ���� AxisCardType.RS485 = 3�A
    /// �� cMachineManager.LoadAxises() �Ϯg���J�ɦ۰ʰt��C
    /// </summary>
    public class cAxis_RS485 : IAxis, IDisposable
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
        // 診斷用：EMO 觸發時 StopAllAxesImmediate 會對所有軸同時下 MotStop，如果同一時間
        // RUN 流程正在對同一軸輪詢 Wait()，兩邊會搶 AsdaB3Controller 內部的 lock——如果剛好
        // 卡進 Modbus 逾時重試，其中一邊就會被卡住很久，且卡住的地方不會檢查 CancellationToken，
        // 導致取消訊號延遲很久才生效（懷疑跟「EMO Reset 後初始化按不動」有關）。這裡記錄每次
        // Wait()/MotStop() 實際花費的時間，超過 SlowCallThresholdMs 就寫一筆 log，正常情況下
        // 應該只要幾十 ms，如果卡住會遠超過這個門檻，方便事後從 log 直接確認是不是卡在這裡。
        private const int SlowCallThresholdMs = 100;

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
        //  RS485 �s�u�]�w
        // ============================

        /// <summary>COM Port �W�١A�Ҧp "COM12"</summary>
        public string PortName { get; set; } = "COM3";

        /// <summary>�j�v�A�w�] 38400</summary>
        public int BaudRate { get; set; } = 38400;

        /// <summary>Modbus �����A�w�] 1</summary>
        public byte SlaveId { get; set; } = 1;

        /// <summary>PUU/mm �ഫ��]���� Scale ���˼ơ^</summary>
        public double PuuPerMm
        {
            get => _controller.PuuPerMm;
            set => _controller.PuuPerMm = value;
        }

        /// <summary>���O�� (ms)</summary>
        public int InPositionTimeout
        {
            get => _controller.InPositionTimeout;
            set => _controller.InPositionTimeout = value;
        }

        // ============================
        //  �D�{���s�W�\��G����s�X�� & �٨�
        // ============================

        /// <summary>����y�ЬO�_���`</summary>
        public bool AbsOk => _controller.AbsOk();

        /// <summary>Ū������s�X�����A�X��</summary>
        public AbsoluteStatusFlags AbsoluteStatus => _controller.ReadAbsoluteStatus();

        /// <summary>���ص�����I</summary>
        public void RebuildAbsoluteOrigin() => _controller.RebuildAbsoluteOrigin();

        /// <summary>�٨����� (true=����, false=��w)</summary>
        public void SetBrake(bool release) => _controller.SetBrake(release);

        /// <summary>�妸Ū���X�ʾ��ַ�</summary>
        public DriverSnapshot ReadSnapshot() => _controller.ReadSnapshot();

        /// <summary>�X�ʾ����A�X��</summary>
        public DriverStatusFlags DriverStatus => _controller.ReadDriverStatus();

        /// <summary>���o���h����]�i���γ~�^</summary>
        public AsdaB3Controller Controller => _controller;

        public cAxis_RS485()
        {
            _modbus = new ModbusRtuClient();
            _controller = new AsdaB3Controller(_modbus);
        }

        // ============================
        //  �s�u / �_�u�]�D�{���I�s�^
        // ============================

        /// <summary>�إ� RS485 �s�u�ê�l���X�ʾ�</summary>
        public void Connect()
        {
            _modbus.Connect(PortName, BaudRate, SlaveId);
            _controller.PuuPerMm = Scale > 0 ? (1.0 / Scale) : 1000.0;
            _controller.Initialize();
            _isConnected = true;
        }

        /// <summary>�_�u</summary>
        public void Disconnect()
        {
            _controller.Dispose();
            _isConnected = false;
        }

        // ============================
        //  IAxis ��@
        // ============================

        public void MotStop(bool isImmediate = false)
        {
            var sw = Stopwatch.StartNew();
            _controller.Stop();
            sw.Stop();
            if (sw.ElapsedMilliseconds >= SlowCallThresholdMs)
                _logger.Warn($"[AxisDiag] {Name}({PortName}) MotStop 耗時 {sw.ElapsedMilliseconds}ms（門檻 {SlowCallThresholdMs}ms，" +
                    "偏長可能是 Modbus 逾時重試，跟其他執行緒同時存取同一軸有關）");
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
            var sw = Stopwatch.StartNew();
            var status = _controller.ReadDriverStatus();
            sw.Stop();
            if (sw.ElapsedMilliseconds >= SlowCallThresholdMs)
                _logger.Warn($"[AxisDiag] {Name}({PortName}) Wait()/ReadDriverStatus 耗時 {sw.ElapsedMilliseconds}ms" +
                    $"（門檻 {SlowCallThresholdMs}ms，偏長可能是 Modbus 逾時重試，跟其他執行緒同時存取同一軸有關）");
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
            // Delta PR mode ���䴩�W�߱Ұʳt�סA����
        }

        public void SetAccTime(double Value)
        {
            _controller.AccelerationTime = (int)(Value * 1000); // �� �� ms
            _controller.ApplySettings();
        }

        public void SetDecTime(double Value)
        {
            _controller.DecelerationTime = (int)(Value * 1000); // �� �� ms
            _controller.ApplySettings();
        }

        public void SetCurve(CurveType Curve)
        {
            this.Curve = Curve;
            // Delta ASDA-B3 PR mode ���䴩�����[��t���u����
        }

        public void SetTrigger(double Position, int CompareMethods)
        {
            // Delta RS485 ���䴩�w�� trigger/compare
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
            return false; // Delta RS485 ���䴩�w�� trigger
        }

        public bool GetEmergency()
        {
            var status = _controller.ReadDriverStatus();
            return status.HasFlag(DriverStatusFlags.Warning);
        }

        public void SetPosition(double Pos)
        {
            // Delta ����s�X���Ҧ��U�q�`���ݭn��ʳ]�w��m
            // �p�ݭ��]���I�i�I�s RebuildAbsoluteOrigin()
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
