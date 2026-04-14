using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Machine.Core.Enums;
using Machine.Core.Interfaces;
using Newtonsoft.Json;

namespace Machine.Core
{
    public class cLight_Net : ILight, IObject_Net
    {
        public LightType Type => LightType.Net;
        [JsonIgnore]
        public int Channel
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
        public string Network_IPAddress
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
        public int Network_Port
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
        public int Network_Timeout
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
        public string PortName
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
        public int BaudRate
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
        public Parity Parity
        {
            get
            {
                return (Parity)TCPComm.Send(this, null); ;
            }
            set
            {
                TCPComm.Send(this, new object[1] { value });
            }
        }
        [JsonIgnore]
        public int DataBits
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
        public StopBits StopBits
        {
            get
            {
                return (StopBits )TCPComm.Send(this, null); ;
            }
            set
            {
                TCPComm.Send(this, new object[1] { value });
            }
        }
        [JsonIgnore]
        public int ReadTimeout
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
        public int WriteTimeout
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

        public int MaxLevel
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
        public string IP { get ; set; }

        public byte GetLuminance()
        {
            return (byte)TCPComm.Send(this, null);
        }

        public bool SetLuminance(byte value, bool Wait = true)
        {
             return (bool)TCPComm.Send(this, new object[2] { value, Wait });
        }
    }
}
