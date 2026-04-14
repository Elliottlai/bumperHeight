using Automation.BDaq;
using Machine.Core.Enums;
using Machine.Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Machine.Core
{
    public class cDO_AdvantechIOCard : IDigitalOutput
    {
        public string Name { get; set; }

        public string UID { get; set; }

        public IOCardType Type => IOCardType.AdvantechIOCard;

        public Type StatusType { set; get; } = typeof(bool);

        public int BoardID { get; set; }

        public int Channel { get; set; }

        public int Bit { get; set; }

        public bool Inverse { get; set; }


        public object GetStatus()
        {


            return AdvantechIOCard.Protect(() =>
            {

                byte ioStatus = 0;
                AdvantechIOCard.DO.ReadBit(Channel, Bit, out ioStatus);
                return Inverse ? ioStatus == 0 : ioStatus != 0;



            });
        }

        public void SetStatus(object Data)
        { 
            AdvantechIOCard.Protect(() =>
            {
                dynamic Status = Convert.ChangeType(Data, StatusType);
                Status = Inverse ? !Status : Status;
                byte ioStatus = (byte)(Status ? 1 : 0);
                ErrorCode a = AdvantechIOCard.DO.WriteBit(Channel, Bit, ioStatus);
            });
        }
    }
}
