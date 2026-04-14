using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Machine.Core.Enums
{
    public enum ModbusFunctionCode
    {
        //0x 16進制
        //Coils = 1bit , Registers = 2byte
        None = 0,
        ReadCoils = 0x1,            //讀出一或多個線圈狀態(ON/OFF)(1bit)
        ReadDiscreteInputs = 0x2,   //讀出一或多個輸入狀態(ON/OFF)(1bit)
        ReadHoldingRegisters = 0x3, //讀出一或多個保持寄存器的值(16bit)
        ReadInputRegisters = 0x4,   //讀出一或多個輸入寄存器的值(16bit)
        WriteSingleCoil = 0x5,      //寫入一個線圈狀態(ON/OFF)
        WriteSingleRegister = 0x6,  //寫入一個寄存器的值(16bit)
        WriteMultipleCoils = 0x0f,  //寫入批量線圈狀態(ON/OFF)
        WriteMultipleRegisters = 0x10,  //寫入批量寄存器的值(16bit)        
    }
}
