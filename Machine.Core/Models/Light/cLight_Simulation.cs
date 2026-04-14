
using Machine.Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO.Ports;
using Machine.Core.Enums;

namespace Machine.Core
{
    public class cLight_Simulation : ILight
    {
        public string Name { set; get; }
        public string UID { set; get; }

        public LightType Type => LightType.Simulation;

        public int Channel { set; get; }

        public string Network_IPAddress { set; get; }
        public int Network_Port { set; get; }
        public int Network_Timeout { set; get; }

        public string PortName { set; get; }
        public int BaudRate { set; get; }
        public Parity Parity { set; get; }
        public int DataBits { set; get; }
        public StopBits StopBits { set; get; }
        public int ReadTimeout { set; get; }
        public int WriteTimeout { set; get; }
        public int MaxLevel { get; set; } = 255;

        private byte Luminance;
        public byte GetLuminance()
            => Luminance;
        public bool SetLuminance(byte value, bool Wait = true)
        {
            Luminance = value;
            return true;
        }

    }
}
