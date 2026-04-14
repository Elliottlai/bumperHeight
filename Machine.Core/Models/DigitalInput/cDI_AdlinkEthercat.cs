using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Machine.Core.Enums;
using Machine.Core.Interfaces;

namespace Machine.Core
{
    class cDI_AdlinkEthercat : IDigitalInput
    {
        public string UID { get; set; }

        public string Name { get; set; }

        public IOCardType Type => IOCardType.AdlinkEthercat;

        public Type StatusType { set; get; } = typeof(bool);

        public int BoardID { get; set; }

        public int Channel { get; set; }

        public int Bit { get; set; }

        public bool Inverse { get; set; }

        public object GetStatus()
        {
            
            uint temp = (uint)AdlinkEtherCATCard.GetInputStatus(Channel, Bit);

            bool success = false;
            if (temp == 1)
                success = true;
            else
                success = false;
            return success;
        }
    }
}
