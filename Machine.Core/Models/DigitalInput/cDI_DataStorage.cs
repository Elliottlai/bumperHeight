using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Machine.Core.Enums;
using Machine.Core.Interfaces;
using Newtonsoft.Json;


namespace Machine.Core
{
     class cDI_DataStorage : IDigitalInput
    {
        public cDI_DataStorage()
        { 
        }

        public IOCardType Type =>  IOCardType.DataStorage;
        public string UID { get; set; }

        public string Name { get; set; }
         

        public Type StatusType { set; get; } = typeof(bool);

        public int BoardID { get; set; }

        public int Channel { get; set; }

        public int Bit { get; set; }

        public bool Inverse { get; set; }
        public object GetStatus()
        {
            return DataStorage.GetData(this); 
        }
    }
}
 