using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Machine.Core.Enums
{
    [Serializable]
    public enum LightType
    {
        Net = 999,
        Simulation = 0,
        SerialPort,
        CobraSlim,
        LightSource,
        

    }
}
