using Machine.Core.Enums;
using Machine.Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Machine.Core
{
    class cDO_DataStorage : IDigitalOutput
    {
        public cDO_DataStorage()
        {

            
        }

        public string Name { get; set; }

        public string UID { get; set; }

        public IOCardType Type => IOCardType.DataStorage;

        public Type StatusType { set; get; } = typeof(bool);

        public int BoardID { get; set; }

        public int Channel { get; set; }

        public int Bit { get; set; }

        public bool Inverse { get; set; }


        public object GetStatus()
        {

            return DataStorage.GetData(this);
        }

        public void SetStatus(object Data)
        {
            DataStorage.SetData(this, Data);
        }

    }
}
