using Machine.Core.Enums;
using Machine.Core.Interfaces;
using System;

namespace Machine.Core
{
    public class cDO_Simulation : IDigitalOutput
    {
        public string Name { set; get; }

        public string UID { set; get; }

        public IOCardType Type => IOCardType.Simulation;

        public Type StatusType { set; get; } = typeof(bool);

        public int BoardID { set; get; }

        public int Channel { set; get; }

        public int Bit { set; get; }

        public bool Inverse { set; get; }

        private object State;
        public object GetStatus()
            => (bool)true;// State;

        public void SetStatus(object Data)
            => State = Data;

    }
}
