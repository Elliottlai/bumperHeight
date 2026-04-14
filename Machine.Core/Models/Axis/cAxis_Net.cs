using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Machine.Core.Enums;
using Machine.Core.Interfaces;
using Newtonsoft.Json;

namespace Machine.Core
{
    class cAxis_Net : IAxis, IObject_Net
    {
        public AxisCardType Type => AxisCardType.Net;
        [JsonIgnore]
        public int AxisID
        {
            get
            {
                return (int)TCPComm.Send(this, null); ;
            }
            set
            {
                TCPComm.Send(this, new object[1] { value });
            }
        }
        [JsonIgnore]
        public int HomeMode
        {
            get
            {
                return (int)TCPComm.Send(this, null); ;
            }
            set
            {
                TCPComm.Send(this, new object[1] { value });
            }
        }
        [JsonIgnore]
        public double HomeSpeed
        {
            get
            {
                return (double)TCPComm.Send(this, null); ;
            }
            set
            {
                TCPComm.Send(this, new object[1] { value });
            }
        }
        [JsonIgnore]
        public double HomeStartSpeed
        {
            get
            {
                return (double)TCPComm.Send(this, null); ;
            }
            set
            {
                TCPComm.Send(this, new object[1] { value });
            }
        }
        [JsonIgnore]
        public double HomeAcc
        {
            get
            {
                return (double)TCPComm.Send(this, null); ;
            }
            set
            {
                TCPComm.Send(this, new object[1] { value });
            }
        }
        [JsonIgnore]
        public double HomeDec
        {
            get
            {
                return (double)TCPComm.Send(this, null); ;
            }
            set
            {
                TCPComm.Send(this, new object[1] { value });
            }
        }
        [JsonIgnore]
        public double HomeBuffer
        {
            get
            {
                return (double)TCPComm.Send(this, null); ;
            }
            set
            {
                TCPComm.Send(this, new object[1] { value });
            }
        }
        [JsonIgnore]
        public double OperationSpeed
        {
            get
            {
                return (double)TCPComm.Send(this, null); ;
            }
            set
            {
                TCPComm.Send(this, new object[1] { value });
            }
        }
        [JsonIgnore]
        public double OperationStartSpeed
        {
            get
            {
                return (double)TCPComm.Send(this, null); ;
            }
            set
            {
                TCPComm.Send(this, new object[1] { value });
            }
        }
        [JsonIgnore]
        public double OperationAcc
        {
            get
            {
                return (double)TCPComm.Send(this, null); ;
            }
            set
            {
                TCPComm.Send(this, new object[1] { value });
            }
        }
        [JsonIgnore]
        public double OperationDec
        {
            get
            {
                return (double)TCPComm.Send(this, null); ;
            }
            set
            {
                TCPComm.Send(this, new object[1] { value });
            }
        }
        [JsonIgnore]
        public double Scale
        {
            get
            {
                return (double)TCPComm.Send(this, null); ;
            }
            set
            {
                TCPComm.Send(this, new object[1] { value });
            }
        }
        [JsonIgnore]
        public double Tolerance
        {
            get
            {
                return (double)TCPComm.Send(this, null); ;
            }
            set
            {
                TCPComm.Send(this, new object[1] { value });
            }
        }
        [JsonIgnore]
        public CurveType Curve
        {
            get
            {
                return (CurveType)TCPComm.Send(this, null); ;
            }
            set
            {
                TCPComm.Send(this, new object[1] { value });
            }
        }
        [JsonIgnore]
        public double SoftwareNLimit
        {
            get
            {
                return (double)TCPComm.Send(this, null); ;
            }
            set
            {
                TCPComm.Send(this, new object[1] { value });
            }
        }
        [JsonIgnore]
        public double SoftwarePLimit
        {
            get
            {
                return (double)TCPComm.Send(this, null); ;
            }
            set
            {
                TCPComm.Send(this, new object[1] { value });
            }
        }

        public string UID { get; set; }
        [JsonIgnore]
        public string Name
        {
            get
            {
                return (string)TCPComm.Send(this, null); ;
            }
            set
            {
                TCPComm.Send(this, new object[1] { value });
            }
        }
        [JsonIgnore]
        public string IP { get; set; }

        public bool GetAlarm()
        {
            return (bool)TCPComm.Send(this, null);
        }

        public bool GetEmergency()
        {
            return (bool)TCPComm.Send(this, null);
        }

        public bool GetINP()
        {
            return (bool)TCPComm.Send(this, null);
        }

        public bool GetIOStatus(int ID)
        {
            return (bool)TCPComm.Send(this, new object[1] { ID });
        }

        public double GetLogicPosition()
        {
            return (double)TCPComm.Send(this, null);
        }

        public bool GetNLimit()
        {
            return (bool)TCPComm.Send(this, null);
        }

        public bool GetOrg()
        {
            return (bool)TCPComm.Send(this, null);
        }

        public bool GetPLimit()
        {
            return (bool)TCPComm.Send(this, null);
        }

        public bool GetRDY()
        {
            return (bool)TCPComm.Send(this, null);
        }

        public double GetRealPosition()
        {
            return (double)TCPComm.Send(this, null);
        }

        public bool GetSVON()
        {
            return (bool)TCPComm.Send(this, null);
        }

        public double GetTargetPosition()
        {
            return (double)TCPComm.Send(this, null);
        }

        public bool GetTrigger()
        {
            return (bool)TCPComm.Send(this, null);
        }

        public bool Home()
        {
            return (bool)TCPComm.Send(this, null);
        }

        public bool MotMoveAbs(double Pos)
        {
            return (bool)TCPComm.Send(this, new object[1] { Pos });
        }

        public bool MotMoveRel(double Pos)
        {
            return (bool)TCPComm.Send(this, new object[1] { Pos });
        }

        public void MotPrevious()
        {
            TCPComm.Send(this, null);
        }

        public void MotStop(bool isImmediate = false)
        {
            TCPComm.Send(this, new object[1] { isImmediate });
        }

        public void ResetError()
        {
            TCPComm.Send(this,null);
        }

        public void SetAccTime(double Value)
        {
            TCPComm.Send(this, new object[1] { Value });
        }

        public void SetCurve(CurveType Curve)
        {
            TCPComm.Send(this, new object[1] { Curve });
        }

        public void SetDecTime(double Value)
        {
            TCPComm.Send(this, new object[1] { Value });
        }

        public void SetDO(int ID, bool OnorOff)
        {
            TCPComm.Send(this, null);
        }

        public void SetMaxVel(double Value)
        {
            TCPComm.Send(this, null);
        }

        public void SetPosition(double Pos)
        {
            TCPComm.Send(this, null);
        }

        public void SetStrVel(double Value)
        {
            TCPComm.Send(this, null);
        }

        public void SetSVON(bool OnorOff)
        {
            TCPComm.Send(this, null);
        }

        public void SetTrigger(double Position, int CompareMethods)
        {
            TCPComm.Send(this, null);
        }

        public bool Wait()
        {
            return (bool)TCPComm.Send(this, null);
        }
    }
}
