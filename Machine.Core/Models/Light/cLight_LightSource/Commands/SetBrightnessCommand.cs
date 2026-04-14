using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynPower.Lights.Litsource
{
    public class SetBrightnessCommand : Command
    {
        public SetBrightnessCommand(byte ch1, byte ch2, byte ch3, byte ch4)
        {
            this.Channel1 = ch1;
            this.Channel2 = ch2;
            this.Channel3 = ch3;
            this.Channel4 = ch4;
        }

        public byte Channel1 { get; private set; }

        public byte Channel2 { get; private set; }

        public byte Channel3 { get; private set; }

        public byte Channel4 { get; private set; }

        public override byte[] Data
        {
            get
            {
                return new byte[] { 31, this.Channel1, this.Channel2, this.Channel3, this.Channel4 };
            }
        }

        public override int ResponseDataTextLength
        {
            get { return 1; }
        }
    }
}
