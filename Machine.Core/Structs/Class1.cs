using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Machine.Core.Structs
{
    [StructLayout(LayoutKind.Explicit)]
    public struct Class1
    {
        [FieldOffset(0)]
        public int A;

        [FieldOffset(0)]
        public byte A0;

        [FieldOffset(1)]
        public byte A1;

        [FieldOffset(2)]
        public byte A2;

        [FieldOffset(3)]
        public byte A3;
        
    }
}
