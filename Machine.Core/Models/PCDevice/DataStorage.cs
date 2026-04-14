using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Machine.Core.Interfaces;

namespace Machine.Core
{
    public class DataStorage
    {
        const int BitLengthPreChannel = 8;
        const int MaxDataLength = 128;
        static object[] data = new object[MaxDataLength];

        static public object GetData(IDigitalInput input)
        {
            int id = CheckIndex(input);
            if (data[id] == null)
                data[id] = Activator.CreateInstance( input.StatusType );            
            return data[id];
        }
        //static public object GetData(IDigitalOutput input)
        //{
        //    int id = CheckIndex(input);
        //    if (data[id] == null)
        //        data[id] = Activator.CreateInstance(input.StatusType);
        //    return data[id];
        //}
        static public void SetData(IDigitalOutput output, object o)
        {
            int id = CheckIndex(output);
            if (data[id] == null)
                data[id] = Activator.CreateInstance(output.StatusType);
            data[id] = o;
        }
        static public void InitData(int id ,object o)
        {
 
         
        }
        static private int CheckIndex(IDigitalInput node)
        {
            int id = node.Channel * BitLengthPreChannel + node.Bit;
            if (id >= MaxDataLength)
            {
                throw new IndexOutOfRangeException($"DataStorageError:Index (C:{node.Channel},B:{node.Bit}) out of range by {MaxDataLength}.");
            }
            return id;
        }
    }
}
