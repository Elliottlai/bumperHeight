using Machine.Core.Enums;
using Machine.Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Machine.Core
{
    public class cDI_Simulation : IDigitalInput
    {
        public string Name { set; get; }

        public string UID { set; get; }

        public IOCardType Type => IOCardType.Simulation;

        public Type StatusType { set; get; } = typeof(bool);

        public int BoardID { set; get; }

        public int Channel { set; get; }

        public int Bit { set; get; }

        public bool Inverse { set; get; }

        public object GetStatus() => true;
           // => Activator.CreateInstance(StatusType); //  false;
    }
}
