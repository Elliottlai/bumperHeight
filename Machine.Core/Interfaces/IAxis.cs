using Machine.Core.Enums;

namespace Machine.Core.Interfaces
{
    public interface IAxis : IAxisArgs
    {
        // stSet Config();
        void MotStop(bool isImmediate = false);
     
        bool MotMoveAbs(double Pos);

        bool MotMoveRel(double Pos);

        bool Wait();

        double GetRealPosition();

        double GetLogicPosition();

        bool Home();

        void SetSVON(bool OnorOff);

        void SetDO(int ID, bool OnorOff);        

        void SetMaxVel(double Value);

        void SetStrVel(double Value);

        void SetAccTime(double Value);

        void SetDecTime(double Value);

        void SetCurve(CurveType Curve);

        void SetTrigger(double Position, int CompareMethods);

        void ResetError();

        bool GetIOStatus(int ID);

        bool GetPLimit();

        bool GetNLimit();

        bool GetOrg();

        bool GetSVON();

        bool GetINP();

        bool GetRDY();

        bool GetAlarm();

        bool GetTrigger();

        bool GetEmergency();

        void SetPosition(double Pos);
             

        void MotPrevious();

        double GetTargetPosition();
        

    }
}
