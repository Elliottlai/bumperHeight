using Machine.Core.Enums;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Machine.Core
{
    /// <summary>
    /// 實作Modbus-TCP通訊協定主要功能
    /// </summary>
    public class ModbusTcp : ModbusBase
    {
        private ushort _transaction = 0;
        private readonly ushort MODBUS_PROTOCOL = 0;
        private readonly ushort MODBUS_DEFAULT_LENGTH = 6;        
        private byte[] CreateReadCommand(byte Unit, ModbusFunctionCode FunctionCode, ushort StartAddress, ushort Quantity)
        {
            using (MemoryStream memory = new MemoryStream())
            {
                memory.WriteByte((byte)(this._transaction >> 8));
                memory.WriteByte((byte)this._transaction);
                memory.WriteByte((byte)(MODBUS_PROTOCOL >> 8));
                memory.WriteByte((byte)MODBUS_PROTOCOL);
                memory.WriteByte((byte)(MODBUS_DEFAULT_LENGTH >> 8));
                memory.WriteByte((byte)MODBUS_DEFAULT_LENGTH);
                memory.WriteByte((byte)Unit);
                memory.WriteByte((byte)FunctionCode);
                memory.WriteByte((byte)(StartAddress >> 8));
                memory.WriteByte((byte)(StartAddress));
                memory.WriteByte((byte)(Quantity >> 8));
                memory.WriteByte((byte)(Quantity));
                this._transaction++;
                return memory.ToArray();
            }
        }        

        public override byte[] ReadCoils(byte Unit, ushort StartAddress, ushort Quantity)
        {
            this.QuantityValidate(StartAddress, Quantity, 1, 2000);
            var requestArray = this.CreateReadCommand(Unit, ModbusFunctionCode.ReadCoils, StartAddress, Quantity);
            return requestArray;
        }        

        public override byte[] ReadDiscreteInputs(byte Unit, ushort StartAddress, ushort Quantity)
        {
            this.QuantityValidate(StartAddress, Quantity, 1, 2000);
            var requestArray = this.CreateReadCommand(Unit, ModbusFunctionCode.ReadDiscreteInputs, StartAddress, Quantity);
            return requestArray;
        }

        public override byte[] ReadHoldingRegisters(byte Unit, ushort StartAddress, ushort Quantity)
        {
            this.QuantityValidate(StartAddress, Quantity, 1, 125);
            var requestArray = this.CreateReadCommand(Unit, ModbusFunctionCode.ReadHoldingRegisters, StartAddress, Quantity);
            return requestArray;
        }

        public override byte[] ReadInputRegisters(byte Unit, ushort StartAddress, ushort Quantity)
        {
            this.QuantityValidate(StartAddress, Quantity, 1, 125);
            var requestArray = this.CreateReadCommand(Unit, ModbusFunctionCode.ReadInputRegisters, StartAddress, Quantity);
            return requestArray;
        }

        public override byte[] WriteSingleCoil(byte Unit, ushort OutputAddress, bool OutputValue)
        {
            ushort outputValue = 0x0000;    //false
            if (OutputValue)
            {
                outputValue = 0xFF00;       //true
            }
            using (MemoryStream memory = new MemoryStream())
            {
                memory.WriteByte((byte)(this._transaction >> 8));
                memory.WriteByte((byte)this._transaction);
                memory.WriteByte((byte)(MODBUS_PROTOCOL >> 8));
                memory.WriteByte((byte)MODBUS_PROTOCOL);
                memory.WriteByte((byte)(MODBUS_DEFAULT_LENGTH >> 8));
                memory.WriteByte((byte)MODBUS_DEFAULT_LENGTH);
                memory.WriteByte((byte)Unit);
                memory.WriteByte((byte)ModbusFunctionCode.WriteSingleCoil);
                memory.WriteByte((byte)(OutputAddress >> 8));
                memory.WriteByte((byte)(OutputAddress));
                memory.WriteByte((byte)(outputValue >> 8));
                memory.WriteByte((byte)(outputValue));
                this._transaction++;

                return memory.ToArray();
            }
        }

        public override byte[] WriteSingleRegister(byte Unit, ushort OutputAddress, short OutputValue)
        {
            using (MemoryStream memory = new MemoryStream())
            {
                memory.WriteByte((byte)(this._transaction >> 8));
                memory.WriteByte((byte)this._transaction);
                memory.WriteByte((byte)(MODBUS_PROTOCOL >> 8));
                memory.WriteByte((byte)MODBUS_PROTOCOL);
                memory.WriteByte((byte)(MODBUS_DEFAULT_LENGTH >> 8));
                memory.WriteByte((byte)MODBUS_DEFAULT_LENGTH);
                memory.WriteByte((byte)Unit);
                memory.WriteByte((byte)ModbusFunctionCode.WriteSingleRegister);
                memory.WriteByte((byte)(OutputAddress >> 8));
                memory.WriteByte((byte)(OutputAddress));
                memory.WriteByte((byte)(OutputValue >> 8));
                memory.WriteByte((byte)(OutputValue));
                this._transaction++;

                return memory.ToArray();
            }
        }

        public override byte[] WriteMultipleCoils(byte Unit, ushort StartAddress, ushort Quantity, byte[] OutputValues)
        {
            this.QuantityValidate(StartAddress, Quantity, 1, 1968);

            byte counter = (byte)OutputValues.Length;
            if (Quantity / 8 != counter)  //每個點1bit
            {
                ModbusException.GetModbusException(0x03);
            }
            ushort dataLength = (ushort)(MODBUS_DEFAULT_LENGTH + OutputValues.Length + 1);
            using (MemoryStream memory = new MemoryStream())
            {
                memory.WriteByte((byte)(this._transaction >> 8));
                memory.WriteByte((byte)this._transaction);
                memory.WriteByte((byte)(MODBUS_PROTOCOL >> 8));
                memory.WriteByte((byte)MODBUS_PROTOCOL);

                memory.WriteByte((byte)(dataLength >> 8));
                memory.WriteByte((byte)dataLength);

                memory.WriteByte((byte)Unit);
                memory.WriteByte((byte)ModbusFunctionCode.WriteMultipleCoils);
                memory.WriteByte((byte)(StartAddress >> 8));
                memory.WriteByte((byte)(StartAddress));
                memory.WriteByte((byte)(Quantity >> 8));
                memory.WriteByte((byte)(Quantity));
                memory.WriteByte((byte)(counter));
                memory.Write(OutputValues, 0, OutputValues.Length);
                this._transaction++;

                return memory.ToArray();
            }
        }

        public override byte[] WriteMultipleRegisters(byte Unit, ushort StartAddress, ushort Quantity, short[] OutputValues)
        {
            this.QuantityValidate(StartAddress, Quantity, 1, 123);

            byte[] outputArray = this.GetByteArray(OutputValues);
            byte counter = (byte)outputArray.Length;

            if (Quantity * 2 != outputArray.Length) //每個點2byte
            {
                ModbusException.GetModbusException(0x02);
            }

            ushort dataLength = (ushort)(MODBUS_DEFAULT_LENGTH + counter + 1);
            using (MemoryStream memory = new MemoryStream())
            {
                memory.WriteByte((byte)(this._transaction >> 8));
                memory.WriteByte((byte)this._transaction);
                memory.WriteByte((byte)(MODBUS_PROTOCOL >> 8));
                memory.WriteByte((byte)MODBUS_PROTOCOL);

                memory.WriteByte((byte)(dataLength >> 8));
                memory.WriteByte((byte)dataLength);

                memory.WriteByte((byte)Unit);
                memory.WriteByte((byte)ModbusFunctionCode.WriteMultipleRegisters);
                memory.WriteByte((byte)(StartAddress >> 8));
                memory.WriteByte((byte)(StartAddress));
                memory.WriteByte((byte)(Quantity >> 8));
                memory.WriteByte((byte)(Quantity));
                memory.WriteByte((byte)(counter));
                memory.Write(outputArray, 0, outputArray.Length);

                this._transaction++;

                return memory.ToArray();
            }
        }

        private byte _functionCodePosition = 7;

        protected override byte FunctionCodePosition
        {
            get { return _functionCodePosition; }
            set { _functionCodePosition = value; }
        }

        public override byte[] GetResult(byte[] RequestArray, byte[] ResponseArray)
        {
            ThrowIfResponseError(ResponseArray);
            var counterPosition = this.FunctionCodePosition + 1;
            var position = ResponseArray[counterPosition];
            var resultArray = new byte[position];
            Array.Copy(ResponseArray, counterPosition + 1, resultArray, 0, resultArray.Length);
            return resultArray;
        }
        private void ThrowIfResponseError(byte[] ResponseArray)
        {
            if((ResponseArray[this.FunctionCodePosition] & 0x80) > 0)
            {
                throw ModbusException.GetModbusException(ResponseArray[this.FunctionCodePosition+1]);
            }
        }
        protected override void CheckDataValidate(byte[] ResponseArray)
        {
            //TCP架構無資料檢查碼
        }        
    }    
}
