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
    
    public class cDO_Net : IDigitalOutput, IObject_Net
    {
        public IOCardType Type => IOCardType.Net;
       [JsonIgnore ]
        public Type StatusType
        {
           get
            {
               
                return(Type) TCPComm.Send(this, null); ;
            }
            set
            {
                TCPComm.Send(this, new object[1] { value });
            }
        }
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
        public int Bit
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
        public bool Inverse
        {

            get
            {
                return (bool)TCPComm.Send(this, null); ;
            }
            set
            {
                TCPComm.Send(this, new object[1] { value });
            }
        }

        public string UID
        {
            get; set;          
        }

        [JsonIgnore]
        public string Name
        {
            get
            {
                return(string) TCPComm.Send(this, null); ;
            }
            set
            {
                TCPComm.Send(this, new object[1] { value });
            }
        }
        [JsonIgnore]
        public string IP { get; set; }

        public object GetStatus()
        {
            return TCPComm.Send(this, null);
        }

        public void SetStatus(object Data)
        {

            TCPComm.Send(this, new object[1] { Data });
        }
    }
}
