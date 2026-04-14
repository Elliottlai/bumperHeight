using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Machine.Core
{
    /// <summary>
    /// 用來處理Modbus相關資料轉換的工具程式
    /// </summary>
    public static class ModbusUtility
    {
        public readonly static string ASCII_START_SYMBOL = ":";
        public readonly static string ASCII_END_SYMBOL = "\r\n";
        private static string[] s_Symbol = new string[] { " ", ",", "-" };

        //public static byte[] HexStringToBytes(string hexString)
        //{
        //    var items = hexString.Split(' ');
        //    List<byte> list = new List<byte>();
        //    foreach (var item in items)
        //    {
        //        list.Add(byte.Parse(item, System.Globalization.NumberStyles.HexNumber));
        //    }
        //    return list.ToArray();
        //}
        public static byte[] HexStringToBytes(string Hex)
        {
            string filter = s_Symbol.Aggregate(Hex, (current, symbol) => current.Replace(symbol, ""));

            return Enumerable.Range(0, filter.Length)
                              .Where(x => x % 2 == 0)
                              .Select(x => Convert.ToByte(filter.Substring(x, 2), 16))
                              .ToArray();
        }
        //public static string ToHexString(byte[] array, string Spacer = "")
        //{
        //    StringBuilder sb = new StringBuilder();
        //    for (int i = 0; i < array.Count(); i++)
        //    {
        //        sb.Append(Convert.ToString(array[i], 16).PadLeft(2, '0'));
        //        if (i < array.Count() - 1)
        //            sb.Append(Spacer);
        //    }
        //    return sb.ToString();
        //}
        public static string ToHexString(byte[] HexArray)
        {
            StringBuilder sb = new StringBuilder();
            foreach (var b in HexArray)
            {
                sb.Append(b.ToString("X2"));
            }
            return sb.ToString();
        }
        public static string ToBinaryString(byte[] array, string Spacer = " ")
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < array.Count(); i++)
            {
                sb.Append(Convert.ToString(array[i], 2).PadLeft(8, '0'));
                if (i < array.Count() - 1)
                    sb.Append(Spacer);
            }
            return sb.ToString();
        }
        public static string ToBinaryString(ushort number, string Spacer = " ")
        {
            StringBuilder sb = new StringBuilder();
            var array = BitConverter.GetBytes(number);
            if (BitConverter.IsLittleEndian)
                array = array.Reverse().ToArray();
            for (int i=0;i< array.Count();i++)
            {
                sb.Append(Convert.ToString(array[i], 2).PadLeft(8, '0'));
                if(i < array.Count()-1)
                    sb.Append(Spacer);
            }
            return sb.ToString();
        }
        public static IEnumerable<ushort> ToUshort(byte[] dataArray)
        {
            if (dataArray.Length % sizeof(ushort) != 0)
                throw new InvalidCastException($"Array size unmatch.({dataArray.Length}/{sizeof(ushort)}");
            List<ushort> arr = new List<ushort>();
            for (int i = 0; i < dataArray.Length; i += sizeof(ushort))
            {
                arr.Add(BitConverter.ToUInt16(dataArray, i));
            }
            return arr.ToArray();
        }
        public static IEnumerable<short> ToShort(byte[] dataArray, bool littleEndian = true)
        {
            if (dataArray.Length % sizeof(short) != 0)
                throw new InvalidCastException($"Array size unmatch.({dataArray.Length}/{sizeof(short)}");
            List<short> arr = new List<short>();
            byte[] tmparr = new byte[sizeof(short)];
            for (int i = 0; i < dataArray.Length; i += sizeof(short))
            {
                Array.Copy(dataArray, i, tmparr, 0, sizeof(short));
                if (littleEndian)
                    tmparr = tmparr.Reverse().ToArray();
                arr.Add(BitConverter.ToInt16(tmparr, 0));
            }
            return arr.ToArray();
        }
        public static IEnumerable<int> ToInt(byte[] dataArray, bool littleEndian = true)
        {
            if (dataArray.Length % sizeof(int) != 0)
                throw new InvalidCastException($"Array size unmatch.({dataArray.Length}/{sizeof(int)}");
            List<int> arr = new List<int>();
            byte[] tmparr = new byte[sizeof(int)];
            for (int i = 0; i < dataArray.Length; i += sizeof(int))
            {
                Array.Copy(dataArray, i, tmparr, 0, sizeof(int));
                if (littleEndian)
                    tmparr = tmparr.Reverse().ToArray();
                arr.Add(BitConverter.ToInt32(tmparr, 0));
            }
            return arr.ToArray();
        }
        public static IEnumerable<float> ToFloat(byte[] dataArray, bool littleEndian = true)
        {
            if (dataArray.Length % sizeof(float) != 0)
                throw new InvalidCastException($"Array size unmatch.({dataArray.Length}/{sizeof(float)}");
            List<float> arr = new List<float>();
            byte[] tmparr = new byte[sizeof(float)];
            for (int i = 0; i < dataArray.Length; i += sizeof(float))
            {
                Array.Copy(dataArray, i, tmparr, 0, sizeof(float));
                if (littleEndian)
                    tmparr = tmparr.Reverse().ToArray();
                arr.Add(BitConverter.ToSingle(tmparr, 0));
            }
            return arr.ToArray();
        }
        public static IEnumerable<double> ToDouble(byte[] dataArray, bool littleEndian = true)
        {
            if (dataArray.Length % sizeof(double) != 0)
                throw new InvalidCastException($"Array size unmatch.({dataArray.Length}/{sizeof(double)}");
            List<double> arr = new List<double>();
            byte[] tmparr = new byte[sizeof(double)];
            for (int i = 0; i < dataArray.Length; i += sizeof(double))
            {
                Array.Copy(dataArray, i, tmparr, 0, sizeof(double));
                if (littleEndian)
                    tmparr = tmparr.Reverse().ToArray();
                arr.Add(BitConverter.ToDouble(tmparr, 0));
            }
            return arr.ToArray();
        }
        //public static string GetStrings(IEnumerable<object> arr)
        //{            
        //    string str = "{";
        //    int index = 0;
        //    foreach (var item in arr)
        //    {
        //        index++;
        //        str += item.ToString();
        //        if (index < arr.Count())
        //            str += ",";
        //    }
        //    str += "}";
        //    return str;
        //}
    }
}
