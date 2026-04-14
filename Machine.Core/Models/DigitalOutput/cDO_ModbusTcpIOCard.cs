using System;
using Machine.Core.Enums;
using Machine.Core.Interfaces;
using System.Threading;

namespace Machine.Core
{
    class cDO_ModbusTcpIOCard : IDigitalOutput
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
                Inverse = ModbusTcpIOCard.modbus[Channel].GetDigitalOutput(Convert.ToUInt16(Bit));
                return Inverse;
            });
            
        }

        public void SetStatus(object Data)
        {

            ModbusTcpIOCard.Protect(() =>
            {
                dynamic Status = Convert.ChangeType(Data, StatusType);
                ModbusTcpIOCard.modbus[Channel].SetDigitalOutput(Convert.ToUInt16(Bit), Status);
            });
            

        }





    }
}
