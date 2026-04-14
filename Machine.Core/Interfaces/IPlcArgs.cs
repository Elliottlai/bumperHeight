using Machine.Core.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Machine.Core.Interfaces
{
    interface IPlcArgs : IComponent, ISerialPortArgs
    {
        PlcType Type { get; }
        string PortName { set; get; }
         
    }
}
