using Machine.Core.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Machine.Core.Interfaces
{
    public interface IDigitalOutputArgs : IComponent
    {
        /// <summary>
        /// 類型 ( SerialPort、Manual、CobraSlim )
        /// </summary>
        IOCardType Type { get; }  

       // int BoardID { set; get; }  

        Type StatusType { set; get; }

        int Channel { set; get; }  

        int Bit { set; get; }  

        bool Inverse { set; get; } 

    }
}
