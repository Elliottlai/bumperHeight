using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Machine.Core.Interfaces
{
    interface IPlcController : IPlcArgs
    {
        //string Read(int FunctionCode);
        //void Write(int FunctionCode);
        
        bool GetStatus(int address);
        void SetStatus(int address, bool status);
        short GetValue(int address);
        void SetValue(int address, short value);

        bool[] GetMultiStatus(int startAddr);
        void SetMultiStatus(int startAddr, bool[] statuses);
        bool[] GetMultiValue(int startAddr);
        void SetMultiValue(int startAddr, short[] values);
    }
}
