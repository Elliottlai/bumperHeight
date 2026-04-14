using Machine.Core.Enums;
using Machine.Core.Interfaces;
using System;

namespace Machine.Core
{
    class cDI_ModbusTcpIOCard : IDigitalInput
    {

        public IOCardType Type => IOCardType.ModbusTcpIOCard;

        public Type StatusType { set; get; } = typeof(bool);
        public int Channel { get; set; }
        public int Bit { get; set; }
        public bool Inverse { get; set; }
        public string UID { get; set; }
        public string Name { get; set; }





        public object GetStatus()
        {
            return ModbusTcpIOCard.Protect(() =>
            {
                Inverse = ModbusTcpIOCard.modbus[Channel].GetDigitalInput(Convert.ToUInt16(Bit));
                return Inverse;
            });

        }
    }
}
