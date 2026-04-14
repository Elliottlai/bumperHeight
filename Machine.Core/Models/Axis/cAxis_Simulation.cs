using Machine.Core.Enums;
using Machine.Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Machine.Core
{
    public class cAxis_Simulation : IAxis
    {
        public AxisCardType Type => AxisCardType.Simulation;

        public int AxisID { get;set; }
        public int HomeMode { get;set; }
        public double HomeSpeed { get;set; }
        public double HomeStartSpeed { get;set; }
        public double HomeAcc { get;set; }
        public double HomeDec { get;set; }
        public double HomeBuffer { get;set; }
        public double OperationSpeed { get;set; }
        public double OperationStartSpeed { get;set; }
        public double OperationAcc { get;set; }
        public double OperationDec { get;set; }
        public double Scale { get;set; }
        public double Tolerance { get;set; }
        public CurveType Curve { get;set; }
        public double SoftwareNLimit { get;set; }
        public double SoftwarePLimit { get;set; }
        public string UID { get;set; }
        public string Name { get; set; }

        public bool GetAlarm()
        {
            return false;
        }

        public bool GetEmergency()
        {
            return false;
        }

        public bool GetINP()
        {
             
            return true;
        }

        public double GetLogicPosition()
        {
            return 0d;
        }

        public bool GetNLimit()
        {
            return false;
        }

        public bool GetOrg()
        {
            return false;
        }

        public bool GetPLimit()
        {
            return false;
        }

        public bool GetRDY()
        {
            return true;
        }

        public double GetRealPosition()
        {
            return 1000d;
        }

        public bool GetSVON()
        {
            return true;
        }

        public bool GetTrigger()
        {
            return true;
        }

        public bool Home()
        {
            return true;
        }

        public bool MotMoveAbs(double Pos)
        {
            return true;
        }

        public bool MotMoveRel(double Pos)
        {
            return false;
        }

        public void MotStop(bool isImmediate = false)
        {
          
        }

 

        public void MotPrevious()
        {
            throw new NotImplementedException();
        }

        public void SetAccTime(double Value)
        {
           
        }

        public void SetCurve(CurveType Curve)
        {
            
        }

        public void SetDecTime(double Value)
        {
           
        }

        public void SetDO(int ID, bool OnorOff)
        {
          
        }

        public void SetMaxVel(double Value)
        {
            
        }

        public void SetPosition(double Pos)
        {
           
        }

        public void SetStrVel(double Value)
        {
            
        }

        public void SetSVON(bool OnorOff)
        {
        
        }

        public void SetTrigger(double Position, int CompareMethods)
        {
         
        }

        public bool Wait()
        {
            return true;
        }

        public double GetTargetPosition()
        {
            return 0;
        }

        public bool GetIOStatus(int ID)
        {
            return false;
        }

        public void ResetError()
        {
            
        }
    }
}
