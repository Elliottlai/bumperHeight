using Machine.Core.Enums;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Machine.Core
{
    /// <summary>
    /// 處理Modbus通訊協定的抽象類別
    /// </summary>
    public abstract class ModbusBase
    {
        public event Action<byte[]> OnReceived;
        protected abstract byte FunctionCodePosition { get; set; }
               
        protected void QuantityValidate(ushort startAddress, ushort quantity, int min, int max)
        {
            if (quantity < min || quantity > max)
                throw new IndexOutOfRangeException($"寫入數量({quantity})超出指定範圍({min}~{max})");
        }
        /// <summary>
        /// 計算線圈數量所需的位元數
        /// </summary>
        protected byte GetByteCount(ushort Quantity)
        {
            return (byte)(Quantity / (sizeof(ushort) * 8));
        }
        /// <summary>
        /// 將輸出資料陣列轉換為Byte陣列
        /// </summary>
        protected byte[] GetByteArray(short[] OutputValues)
        {
            List<byte> data = new List<byte>();
            foreach (var item in OutputValues)
            {
                //foreach (var bytevalue in BitConverter.GetBytes(item).Reverse())
                foreach (var bytevalue in BitConverter.GetBytes(item))
                {
                    data.Add(bytevalue);
                }
            }
            return data.ToArray();
        }
        //protected abstract byte[] CalculateCRC(byte[] source);
        public abstract byte[] ReadCoils(byte Unit, ushort StartAddress, ushort Quantity);
        public abstract byte[] ReadDiscreteInputs(byte Unit, ushort StartAddress, ushort Quantity);
        public abstract byte[] ReadHoldingRegisters(byte Unit, ushort StartAddress, ushort Quantity);
        public abstract byte[] ReadInputRegisters(byte Unit, ushort StartAddress, ushort Quantity);
        public abstract byte[] WriteSingleCoil(byte Unit, ushort OutputAddress, bool OutputValue);
        public abstract byte[] WriteSingleRegister(byte Unit, ushort OutputAddress, short OutputValue);
        public abstract byte[] WriteMultipleCoils(byte Unit, ushort StartAddress, ushort Quantity, byte[] OutputValues);
        public abstract byte[] WriteMultipleRegisters(byte Unit, ushort StartAddress, ushort Quantity, short[] OutputValues);
        public abstract byte[] GetResult(byte[] RequestArray, byte[] ResponseArray);
        protected abstract void CheckDataValidate(byte[] ResponseArray);       

    }
}
