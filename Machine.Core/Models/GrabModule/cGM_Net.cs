using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Machine.Core.Enums;
using Machine.Core.Interfaces;
using Newtonsoft.Json;

namespace Machine.Core.Models.GrabModule
{
    [Serializable]
    public class cGM_Net : ICamera,IObject_Net
    {
        public GrabModuleType Type => GrabModuleType.Net;
        [JsonIgnore]
        public string CCD_Name
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
        public string CCD_ID
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
        public double Gain
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
        public double ExposureTime
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
        public double Rate
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
        public int PixelBytes
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
        public int FrameWidth
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
        public int FrameHeight
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
        public double PixelWidth
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
        public double PixelHeight
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
        public string ConfigFileName
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

        public string UID
        {
            get;
     
            set;
    
        }
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

        public string IP { get  ; set  ; }
        [JsonIgnore]
        public int BufWidth
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
        public int BufHeight
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
        public object FunctionCall(GMExpandFunction func, params object[] value)
        {
            /*
            object []param = new object[value.Length + 1];
            param[0] = func;
            for (int i = 1; i < param.Length; i++)
                param[i] = value[i-1 ];

            return TCPComm.Send(this , param  );
            */
            return TCPComm.Send(this, new object[2] { func, value });
        }

        public IntPtr[] GetBufAddress(int index = -1)
        {
            throw new NotImplementedException();
        }

        public IntPtr[] GetCurrentFrame()
        {
            throw new NotImplementedException();
        }

        public object GetFeatureValue(GMExpandParamter Param)
        {
            return TCPComm.Send(this, new object[1] { Param });
        }
 

        public bool Init()
        {
            return (bool) TCPComm.Send(this, null );
        }

        public bool SetFeatureValue(GMExpandParamter Param, params object[] value)
        {
            /*
            object[] param = new object[value.Length + 1];
            param[0] = Param;
            for (int i = 1; i < param.Length ; i++)
                param[i] = value[i - 1];

            return (bool)TCPComm.Send(this, param);*/
            return (bool)TCPComm.Send(this, new object[2] { Param, value }); 

        }

 

        public void Start(int BufIndex = -1)
        {
            TCPComm.Send(this, new object[1]  { BufIndex});
        }

        public void Stop()
        {
            TCPComm.Send(this, null);
        }
    }
}
