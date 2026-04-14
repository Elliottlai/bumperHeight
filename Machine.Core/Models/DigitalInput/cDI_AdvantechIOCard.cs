using Machine.Core.Enums;
using Machine.Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Machine.Core
{
    public class cDI_AdvantechIOCard : IDigitalInput
    {
        public IOCardType Type => IOCardType.AdvantechIOCard;

        public Type StatusType { set; get; } = typeof(bool);

        public int Channel { get; set; }
        public int Bit { get; set; }
        public bool Inverse { get; set; }
        public string UID { get; set; }
        public string Name { get; set; }


        public object GetStatus()
        {
            //byte ioStatus = 0;
            //AdvantechIOCard.LockProtect();
            //AdvantechIOCard.DI.ReadBit(Channel, Bit, out ioStatus);
            //AdvantechIOCard.UnLock();

            //return Inverse ? ioStatus == 0 : ioStatus != 0;

            return AdvantechIOCard.Protect(() => {

                byte ioStatus = 0;                
                AdvantechIOCard.DI.ReadBit(Channel, Bit, out ioStatus);        
                return Inverse ? ioStatus == 0 : ioStatus != 0;


            });
        }
    }
}
