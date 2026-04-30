using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DeltaAxis_RS485.Helper
{
    public static class ModbusWordHelper
    {
        /// <summary>
        /// 將 32-bit 有號整數拆成 Low Word / High Word
        /// </summary>
        public static (ushort low, ushort high) SplitInt32(int value)
        {
            unchecked
            {
                uint raw = (uint)value;
                ushort low = (ushort)(raw & 0xFFFF);
                ushort high = (ushort)((raw >> 16) & 0xFFFF);
                return (low, high);
            }
        }

        /// <summary>
        /// 將 Low Word / High Word 組回 32-bit 有號整數
        /// </summary>
        public static int CombineInt32(ushort low, ushort high)
        {
            uint raw = ((uint)high << 16) | low;
            return unchecked((int)raw);
        }
    }
}
