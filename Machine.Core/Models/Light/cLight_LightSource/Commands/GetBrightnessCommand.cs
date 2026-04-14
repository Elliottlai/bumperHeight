using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynPower.Lights.Litsource
{
    public class GetBrightnessCommand : Command
    {
        public override byte[] Data
        {
            get { return new byte[] { 0X20 }; }
        }

        public override int ResponseDataTextLength
        {
            get { return 4; }
        }
    }
}
