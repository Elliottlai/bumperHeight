using Machine.Core.Enums;
using Machine.Core.Interfaces;

namespace Slot_Inspection.Services;

/// <summary>
/// Y 軸的安全外殼：把真正的 Y 軸 <see cref="IAxis"/> 包起來，唯一的差別是
/// <see cref="MotMoveAbs"/> / <see cref="MotMoveRel"/> / <see cref="MotPrevious"/> 送出前，
/// 一定會先跑一次「ZL/ZR 是否在安全高度」硬檢查，不通過就丟
/// <see cref="MachineSafetyException"/>，指令不會送到驅動器。
///
/// 這是「結構性保證」——不管呼叫端是誰、有沒有記得先呼叫 EnsureZSafeBeforeYMoveAsync()，
/// 只要透過 <c>MachineController.Axes["AxisY"]</c> 發 Y 軸移動，就一定會過這道閘。
/// <c>EnsureZSafeBeforeYMoveAsync()</c> 仍是建議的「友善前置」（會跳視窗問要不要自動升軸），
/// 這道閘只是最後一層、不講情面的攔截，防止「有人忘了呼叫前置」。
///
/// 其餘所有 IAxis / IAxisArgs / IComponent 成員都原封不動轉呼內層真正的軸。
/// </summary>
internal sealed class ZSafeGuardedYAxis(IAxis inner, System.Func<(bool ok, string reason)> zSafeGate) : IAxis
{
    /// <summary>被包起來的真正 Y 軸（InitAxesAsync 重新連線時要拿它做 RS485 Connect / 設 Homing 速度）。</summary>
    public IAxis Inner => inner;

    private void AssertZSafe(string op)
    {
        var (ok, reason) = zSafeGate();
        if (!ok)
            throw new MachineSafetyException(
                $"Y 軸 {op} 被安全閘攔截：{reason}。" +
                "發出 Y 軸移動前必須先確認 ZL/ZR 已在安全高度（呼叫 EnsureZSafeBeforeYMoveAsync）。");
    }

    // ── 受安全閘保護的三個移動指令 ──
    public bool MotMoveAbs(double Pos) { AssertZSafe("MotMoveAbs"); return inner.MotMoveAbs(Pos); }
    public bool MotMoveRel(double Pos) { AssertZSafe("MotMoveRel"); return inner.MotMoveRel(Pos); }
    public void MotPrevious()          { AssertZSafe("MotPrevious"); inner.MotPrevious(); }

    // ── 其餘 IAxis 成員：原封不動轉呼 inner ──
    public void MotStop(bool isImmediate = false) => inner.MotStop(isImmediate);
    public bool Wait() => inner.Wait();
    public double GetRealPosition() => inner.GetRealPosition();
    public double GetLogicPosition() => inner.GetLogicPosition();
    public bool Home() => inner.Home();
    public void SetSVON(bool OnorOff) => inner.SetSVON(OnorOff);
    public void SetDO(int ID, bool OnorOff) => inner.SetDO(ID, OnorOff);
    public void SetMaxVel(double Value) => inner.SetMaxVel(Value);
    public void SetStrVel(double Value) => inner.SetStrVel(Value);
    public void SetAccTime(double Value) => inner.SetAccTime(Value);
    public void SetDecTime(double Value) => inner.SetDecTime(Value);
    public void SetCurve(CurveType Curve) => inner.SetCurve(Curve);
    public void SetTrigger(double Position, int CompareMethods) => inner.SetTrigger(Position, CompareMethods);
    public void ResetError() => inner.ResetError();
    public bool GetIOStatus(int ID) => inner.GetIOStatus(ID);
    public bool GetPLimit() => inner.GetPLimit();
    public bool GetNLimit() => inner.GetNLimit();
    public bool GetOrg() => inner.GetOrg();
    public bool GetSVON() => inner.GetSVON();
    public bool GetINP() => inner.GetINP();
    public bool GetRDY() => inner.GetRDY();
    public bool GetAlarm() => inner.GetAlarm();
    public bool GetTrigger() => inner.GetTrigger();
    public bool GetEmergency() => inner.GetEmergency();
    public void SetPosition(double Pos) => inner.SetPosition(Pos);
    public double GetTargetPosition() => inner.GetTargetPosition();

    // ── IAxisArgs / IComponent：代理到 inner ──
    public AxisCardType Type => inner.Type;
    public int AxisID { get => inner.AxisID; set => inner.AxisID = value; }
    public int HomeMode { get => inner.HomeMode; set => inner.HomeMode = value; }
    public double HomeSpeed { get => inner.HomeSpeed; set => inner.HomeSpeed = value; }
    public double HomeStartSpeed { get => inner.HomeStartSpeed; set => inner.HomeStartSpeed = value; }
    public double HomeAcc { get => inner.HomeAcc; set => inner.HomeAcc = value; }
    public double HomeDec { get => inner.HomeDec; set => inner.HomeDec = value; }
    public double HomeBuffer { get => inner.HomeBuffer; set => inner.HomeBuffer = value; }
    public double OperationSpeed { get => inner.OperationSpeed; set => inner.OperationSpeed = value; }
    public double OperationStartSpeed { get => inner.OperationStartSpeed; set => inner.OperationStartSpeed = value; }
    public double OperationAcc { get => inner.OperationAcc; set => inner.OperationAcc = value; }
    public double OperationDec { get => inner.OperationDec; set => inner.OperationDec = value; }
    public double Scale { get => inner.Scale; set => inner.Scale = value; }
    public double Tolerance { get => inner.Tolerance; set => inner.Tolerance = value; }
    public CurveType Curve { get => inner.Curve; set => inner.Curve = value; }
    public double SoftwareNLimit { get => inner.SoftwareNLimit; set => inner.SoftwareNLimit = value; }
    public double SoftwarePLimit { get => inner.SoftwarePLimit; set => inner.SoftwarePLimit = value; }
    public string UID { get => inner.UID; set => inner.UID = value; }
    public string Name { get => inner.Name; set => inner.Name = value; }
}
