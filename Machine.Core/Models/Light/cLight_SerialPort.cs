using Machine.Core.Enums;
using Machine.Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Machine.Core
{
    public class cLight_SerialPort : ILight
    {
        public string UID { get; set; }

        public string Name { get; set; }

        public LightType Type => LightType.SerialPort;

        public int Channel { get; set; }

        public string Network_IPAddress { get; set; } = "10.0.0.10";
        public int Network_Port { get; set; } = 2000;
        public int Network_Timeout { get; set; }
        public int MaxLevel { get; set; } = 255;

        public string PortName
        {
            get => Comport?.PortName ?? string.Empty;
            set => Comport = SerialPortManager.GetSerialPort(value);
        }

        public int BaudRate { get; set; } = 9600;
        public Parity Parity { get; set; } = Parity.None;
        public int DataBits { get; set; } = 8;
        public StopBits StopBits { get; set; } = StopBits.One;
        public int ReadTimeout { get; set; } = 1000;
        public int WriteTimeout { get; set; } = 1000;

        protected SerialPort Comport { set; get; }


        private byte Luminance;
        public bool SetLuminance(byte value, bool Wait = true)
        {
            if (value > 0 && Luminance.Equals(value))
                return false;

            //string recive = string.Empty;

            string command = this.GetCommand(value);
            //string check = this.GetConfirmCommand(value);

            Comport?.TryWrite(command, this);

            bool bSuccess = true;
            try
            {
                string buf = Comport?.ReadLine();
                Luminance = value;
                if (Wait)
                    Task.Delay(100);

            }
            catch (Exception ex)
            {
                bSuccess = false;
                //throw new Exception("SetLuminance error");
            }


            return bSuccess;


            //SerialPortManager.UnLock();
            //while (!this.IsTimeout)
            //{
            //    try
            //    {

            //        if (string.IsNullOrEmpty(recive) || recive == check)
            //        {
            //        }

            //        if (this.IsTimeout)
            //            throw new TimeoutException();
            //    }
            //    catch (Exception ex)
            //    {
            //        string msg = string.Format("燈源嘗試變更亮度失敗, Channel : {0} \n 逾時 : {1} ms。\nError Recive : {2}", channel, this.MillisecondsTimeout, recive);
            //        if (ex is TimeoutException)
            //            throw new TimeoutException(msg);
            //        else
            //            throw new Exception(ex.ToString() + "\n" + msg);
            //    }
            //}


        }
        public byte GetLuminance()
            => Luminance;


        private string SignEnd = "\n";
        private string SignNewLine = "\r";
        private string GetCommand(int rank)
            => $"{Channel},{rank}{SignNewLine}{SignEnd}";

        private string GetConfirmCommand(int rank)
            => $"{Channel},{rank}";

    }
}
