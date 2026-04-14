using Machine.Core.Enums;
using Machine.Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Machine.Core
{
    public class cDO_AdlinkEthercat : IDigitalOutput
    {
        public string Name { get; set; }

        public string UID { get; set; }

        public IOCardType Type => IOCardType.AdlinkEthercat;

        public Type StatusType { set; get; } = typeof(bool);

        public int Bit { get; set; }

        // public int BoardID { get; set; }

        public int Channel { get; set; }

        public bool Inverse { get; set; }

         bool nowstatus = false;

        public object GetStatus()
        {
            //return nowstatus;
            uint temp = (uint)AdlinkEtherCATCard.GetOutputStatus(Channel, Bit);
            bool success = false;
            if (temp == 1)
                success = true;
            else
                success = false;
            return success;
        }

        public void SetStatus(object Data)
        {
            uint temp ;
            if ((bool)Data == true)
                temp = 1;
            else
                temp = 0;
            AdlinkEtherCATCard.SetOutputStatus(Channel, Bit, temp);
            nowstatus = (bool)Data;
        }
    }
}
