using Machine.Core.Enums;
using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Machine.Core.Interfaces
{
    public interface ILightArgs : IComponent, ISerialPortArgs
    {
        /// <summary>
        /// 類型 ( SerialPort、Manual、CobraSlim )
        /// </summary>
        LightType Type { get; }

        /// <summary>
        /// 通道
        /// </summary>
        int Channel { set; get; }

        #region Network
        string Network_IPAddress { set; get; }

        int Network_Port { set; get; }

        int Network_Timeout { set; get; }

        #endregion

        string PortName { set; get; }



    }
}
