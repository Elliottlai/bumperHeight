using Machine.Core.Enums;
using Machine.Core.Interfaces;
using DeltaAxis_RS485.Helper;
using DeltaAxis_RS485.Models;

namespace DeltaAxis_RS485.Services;

/// <summary>
/// 將 AsdaB3Controller 適配為 Machine.Core 的 IAxis 介面
/// </summary>
public class AsdaB3AxisAdapter : IAxis, IDisposable
{
    private readonly AsdaB3Controller _controller;
    private bool _disposed;

    public AsdaB3AxisAdapter(AsdaB3Controller controller, string uid, string name)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        UID = uid;
        Name = name;
    }

    // ============================
    //  IComponent
    // ============================
    public string UID { get; set; }
    public string Name { get; set; }

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
    public double Scale { get; set; } = 1.0;
    public double Tolerance { get; set; } = 0.01;
    public CurveType Curve { get; set; } = CurveType.T_Curve;
    public double SoftwareNLimit { get; set; }
    public double SoftwarePLimit { get; set; }

    // ============================
    //  IAxis 實作
    // ============================

    public void MotStop(bool isImmediate = false)
    {
        // ASDA-B3 目前無獨立急停暫存器，透過 DI 虛擬暫存器控制
        _controller.ServoOff();
    }

    public bool MotMoveAbs(double Pos)
    {
        _controller.MoveToPositionMm(Pos);
        return true;
    }

    public bool MotMoveRel(double Pos)
    {
        double current = GetRealPosition();
        _controller.MoveToPositionMm(current + Pos);
        return true;
    }

    public bool Wait()
    {
        // 檢查是否已到位（非阻塞式查詢）
        return _controller.IsServoOn && !_controller.HasAlarm();
    }

    public double GetRealPosition()
    {
        int puu = _controller.GetMultiTurnPosition();
        return puu / _controller.PuuPerMm;
    }

    public double GetLogicPosition() => GetRealPosition();

    public bool Home()
    {
        // ASDA-B3 使用 absolute encoder，重建原點即可
        _controller.RebuildAbsoluteOrigin();
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
        // 透過 DI 虛擬暫存器模擬 DO
        ushort value = OnorOff ? (ushort)(1 << ID) : (ushort)0;
        // [TODO] 依實際需求實作
    }

    public void SetMaxVel(double Value)
    {
        _controller.TargetSpeed = (int)(Value * 60); // mm/s → rpm (簡化換算)
    }

    public void SetStrVel(double Value)
    {
        // ASDA-B3 PR mode 不支援獨立起始速度
    }

    public void SetAccTime(double Value)
    {
        _controller.AccelerationTime = (int)(Value * 1000); // sec → ms
    }

    public void SetDecTime(double Value)
    {
        _controller.DecelerationTime = (int)(Value * 1000); // sec → ms
    }

    public void SetCurve(CurveType Curve)
    {
        this.Curve = Curve;
        // ASDA-B3 PR mode 加減速曲線由驅動器內部決定
    }

    public void SetTrigger(double Position, int CompareMethods)
    {
        // ASDA-B3 不支援比較觸發
        throw new NotSupportedException("ASDA-B3 RS485 模式不支援位置比較觸發");
    }

    public void ResetError()
    {
        _controller.ClearAlarm();
    }

    public bool GetIOStatus(int ID) => false;
    public bool GetPLimit() => false; // [TODO] 依實際狀態暫存器實作
    public bool GetNLimit() => false;
    public bool GetOrg() => _controller.AbsOk();
    public bool GetSVON() => _controller.IsServoOn;
    public bool GetINP() => true; // 到位由 WaitInPosition 處理
    public bool GetRDY() => _controller.IsServoOn && !_controller.HasAlarm();
    public bool GetAlarm() => _controller.HasAlarm();
    public bool GetTrigger() => false;
    public bool GetEmergency() => false;

    public void SetPosition(double Pos)
    {
        // Absolute encoder 不需手動設定位置
    }

    public void MotPrevious()
    {
        // [TODO] 記錄上次目標位置並回移
    }

    public double GetTargetPosition() => GetRealPosition();

    // ============================
    //  IDisposable
    // ============================
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _controller.Dispose();
        GC.SuppressFinalize(this);
    }
}