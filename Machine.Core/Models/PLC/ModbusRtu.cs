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
    /// 實作Modbus-RTU通訊協定主要功能
    /// </summary>
    public class ModbusRtu : ModbusBase
    {
        private byte[] CreateReadCommand(byte Unit, ModbusFunctionCode FunctionCode, ushort StartAddress, ushort Quantity)
        {
            using (MemoryStream memory = new MemoryStream())
            {
                memory.WriteByte((byte)Unit);
                memory.WriteByte((byte)FunctionCode);
                memory.WriteByte((byte)(StartAddress >> 8));    //高位Byte
                memory.WriteByte((byte)(StartAddress));         //低位Byte
                memory.WriteByte((byte)(Quantity >> 8));
                memory.WriteByte((byte)(Quantity));

                var crcArray = CalculateCRC(memory.ToArray());
                memory.Write(crcArray, 0, crcArray.Length);
                return memory.ToArray();
            }
        }

        private byte[] CalculateCRC(byte[] data)
        {
            ushort crc = 0xffff;
            //Debug.WriteLine($"Base\t{ModbusUtility.ToBinaryString(crc)}");
            foreach (var unit in data)
            {
                //只取ushort低位(1byte)做XOR
                ushort crcLo = (ushort)((crc & 0x00ff) ^ unit);  //取低位做XOR
                ushort crcHi = (ushort)(crc & 0xff00);  //高位
                crc = (ushort)(crcHi + crcLo);

                //ushort crcLo = (ushort)((byte)crc ^ unit);  //取低位做XOR
                //ushort crcHi = (ushort)((crc >> 8) << 8);  //高位
                //crc = (ushort)(crcHi + crcLo);

                //Debug.WriteLine($"^0x{ModbusUtility.ToHexString(new byte[] { unit })}\t{ModbusUtility.ToBinaryString(crc)}");
                for (int i = 0; i < 8; i++)
                {
                    bool cf = (crc & 1) == 1;   //進位符
                    crc = (ushort)(crc >> 1);   //右移1個bit
                    //Debug.WriteLine($">>1  \t{ModbusUtility.ToBinaryString(crc)}");
                    if (cf)
                    {
                        crc = (ushort)(crc ^ 0xA001);
                        //Debug.WriteLine($"^0xA001\t{ModbusUtility.ToBinaryString(crc)}");
                    }
                }
                //Debug.WriteLine($"-------------------------------");
            }
            return BitConverter.GetBytes(crc);
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
                memory.WriteByte((byte)Unit);
                memory.WriteByte((byte)ModbusFunctionCode.WriteSingleCoil);
                memory.WriteByte((byte)(OutputAddress >> 8));
                memory.WriteByte((byte)(OutputAddress));
                memory.WriteByte((byte)(outputValue >> 8));
                memory.WriteByte((byte)(outputValue));

                var crcArray = this.CalculateCRC(memory.ToArray());
                memory.Write(crcArray, 0, crcArray.Length);
                return memory.ToArray();
            }
        }

        public override byte[] WriteSingleRegister(byte Unit, ushort OutputAddress, short OutputValue)
        {
            using (MemoryStream memory = new MemoryStream())
            {
                memory.WriteByte((byte)Unit);
                memory.WriteByte((byte)ModbusFunctionCode.WriteSingleRegister);
                memory.WriteByte((byte)(OutputAddress >> 8));
                memory.WriteByte((byte)(OutputAddress));
                memory.WriteByte((byte)(OutputValue >> 8));
                memory.WriteByte((byte)(OutputValue));

                var crcArray = this.CalculateCRC(memory.ToArray());
                memory.Write(crcArray, 0, crcArray.Length);
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
            using (MemoryStream memory = new MemoryStream())
            {
                memory.WriteByte((byte)Unit);
                memory.WriteByte((byte)ModbusFunctionCode.WriteMultipleCoils);
                memory.WriteByte((byte)(StartAddress >> 8));
                memory.WriteByte((byte)(StartAddress));
                memory.WriteByte((byte)(Quantity >> 8));
                memory.WriteByte((byte)(Quantity));
                memory.WriteByte((byte)(counter));
                memory.Write(OutputValues, 0, OutputValues.Length);

                var crcArray = this.CalculateCRC(memory.ToArray());
                memory.Write(crcArray, 0, crcArray.Length);
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
                ModbusException.GetModbusException(0x03);
            }

            using (MemoryStream memory = new MemoryStream())
            {
                memory.WriteByte((byte)Unit);
                memory.WriteByte((byte)ModbusFunctionCode.WriteMultipleRegisters);
                memory.WriteByte((byte)(StartAddress >> 8));
                memory.WriteByte((byte)(StartAddress));
                memory.WriteByte((byte)(Quantity >> 8));
                memory.WriteByte((byte)(Quantity));
                memory.WriteByte((byte)(counter));
                memory.Write(outputArray, 0, outputArray.Length);

                var crcArray = this.CalculateCRC(memory.ToArray());
                memory.Write(crcArray, 0, crcArray.Length);
                return memory.ToArray();
            }
        }

        private byte _functionCodePosition = 1;

        protected override byte FunctionCodePosition
        {
            get { return _functionCodePosition; }
            set { _functionCodePosition = value; }
        }

        public override byte[] GetResult(byte[] RequestArray, byte[] ResponseArray)
        {
            var counterPosition = this.FunctionCodePosition + 1;
            var position = ResponseArray[counterPosition];
            var resultArray = new byte[position];
            Array.Copy(ResponseArray, counterPosition + 1, resultArray, 0, resultArray.Length);
            return resultArray;
        }

        protected override void CheckDataValidate(byte[] ResponseArray)
        {
            var sourceCrcArray = new byte[2];
            Array.Copy(ResponseArray, ResponseArray.Length - 2, sourceCrcArray, 0, sourceCrcArray.Length);
            var sourceDataArray = new byte[ResponseArray.Length - 2];
            Array.Copy(ResponseArray, 0, sourceDataArray, 0, sourceDataArray.Length);
            var destinationCrcArray = CalculateCRC(sourceDataArray);
            if (!sourceCrcArray.SequenceEqual(destinationCrcArray))
            {
                throw new ModbusException("CRC Validate Fail");
            }
        }        
    }    
}
