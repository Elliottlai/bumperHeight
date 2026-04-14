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
    /// <summary>
    /// 序列埠光控器模組
    /// </summary>
    public class cLight_SerialPortControlBox : ILightController, IDisposable
    {
        public string UID { get; set; }

        public string Name { get; set; }

        public LightType Type => LightType.SerialPort;

        #region NotUse
        [Obsolete("於光控器模組內無效", false)]
        public int Channel { get; set; }        

        public string Network_IPAddress { get; set; } = "10.0.0.10";
        public int Network_Port { get; set; } = 2000;
        public int Network_Timeout { get; set; }
        #endregion

        //public bool IsEnable { get; set; }
        public string PortName
        {
            get => Comport?.PortName ?? string.Empty;
            set => Comport = SerialPortManager.GetSerialPort(value);
        } 

        public int BaudRate { get; set; } = 115200;
        public Parity Parity { get; set; } = Parity.None;
        public int DataBits { get; set; } = 8;
        public StopBits StopBits { get; set; } = StopBits.One;
        public int ReadTimeout { get; set; } = 1000;
        public int WriteTimeout { get; set; } = 1000;

        protected SerialPort Comport { set; get; }

        public int MaxLevel { get; set; } = 1023;

        private int[] ChannelsID;
        private int[] Luminance;

        public cLight_SerialPortControlBox(string portName, int channelCount)
        {
            try
            {
                PortName = portName;
                Luminance = new int[channelCount];

                ChannelsID = new int[channelCount];
                for (byte i = 0; i < channelCount; i++)
                    ChannelsID[i] = i + 1;
            }
            catch(Exception ex)
            {
                throw new Exception($"燈源初始化失敗：{ex.Message}");
            }           
        }
        public cLight_SerialPortControlBox(string portName, ISerialPortArgs args, int channelCount)
            : this(portName, channelCount)
        {
            BaudRate = args.BaudRate;
            Parity = args.Parity;
            DataBits = args.DataBits;
            StopBits = args.StopBits;
            ReadTimeout = args.ReadTimeout;
            WriteTimeout = args.WriteTimeout;
            MaxLevel = args.MaxLevel;
            Comport.BaudRate = args.BaudRate;
            Comport.Parity = args.Parity;
            Comport.DataBits = args.DataBits;
            Comport.StopBits = args.StopBits;
            Comport.ReadTimeout = args.ReadTimeout;
            Comport.WriteTimeout = args.WriteTimeout;
            

        }
        public int GetChannelCount()
        {
            return Luminance.Count();
        }
        public async void SetLuminance(int channel, int value)
        {
            this.ThrowIfLuminanceOutOfRange(value);

            string command = this.GetCommand(channel, value);
            //string check = this.GetConfirmCommand(value);

            Comport?.TryWrite(command, this);

            //Thread.Sleep(100);
            Luminance[channel-1] =  (value);

            await Task.Delay(100);
        }

        public async void SetLuminance(int[] ranks, bool changeOnly = true)
        {
            foreach (var rank in ranks)
                this.ThrowIfLuminanceOutOfRange(rank);
            if (ranks.Count() != GetChannelCount())
                throw new InvalidOperationException("光源設置數量與總頻道數不符");

            //數值有變更再進行
            //if(changeOnly)
            //{
            //    bool isChangeValue = default(bool);
            //    for (int i = 0; i < GetChannelCount(); i++)
            //        if (Luminance[i] != ranks[i])
            //            isChangeValue = true;

            //    if (!isChangeValue) return;
            //}           

            string command = this.GetCommand(ranks, changeOnly);

            Comport?.TryWrite(command, this);
            

            //Luminance = Array.ConvertAll(ranks, new Converter<int, byte>(Convert.ToByte));
            Luminance = ranks;

            await Task.Delay(100);
        }

        public int GetLuminance(int channel)
        {
            return Luminance[channel];
        }

        public int[] GetLuminance()
        {
            return Luminance.ToArray();
        }
        // Array.ConvertAll(Luminance, new Converter<byte, int>(Convert.ToInt32));

        private const string SignEnd = "\n";
        private const string SignNewLine = "\r";

        private string GetCommand(int channel, int rank)
            => $"{channel},{rank}{SignNewLine}{SignEnd}";

        private string GetCommand(int[] ranks, bool changeOnly = true)
        {
            string str = "";
            for(int i=0;i< ranks.Count(); i++)
            {
                if (changeOnly && Luminance[i] == ranks[i]) continue;   //僅變更亮度與原來不同的頻道
                str += $"{ ChannelsID[i]},{ranks[i]}";
                if (i < ranks.Count() - 1)
                    str += ",";
            }

            return $"{str}{SignNewLine}{SignEnd}";            
        }

        private string GetConfirmCommand(int channel, int rank)
            => $"{channel},{rank}";

        private string GetConfirmCommand(int[] ranks)
        {
            string str = "";
            for (int i = 0; i < ranks.Count(); i++)
            {
                str += $"{ ChannelsID[i]},{ranks[i]}";
                if (i < ranks.Count() - 1)
                    str += ",";
            }
            return str;
        }
        public void TurnOff()
        {
            SetLuminance(new int[GetChannelCount()], false);
        }
        private void ThrowIfLuminanceOutOfRange(int value)
        {
            if (value < 0 || value > MaxLevel)
                throw new ArgumentOutOfRangeException($"光源亮度設定值超出範圍(0~{MaxLevel})");
        }
        public void Dispose()
        {
            //if (!this.IsEnable) return;
            TurnOff();
            this.Comport.Dispose();
        }
        public override string ToString()
        {
            string str = $"{Name}({PortName})=[";
            foreach (var rank in Luminance)
            {
                str += $"{rank}";
                if (rank != Luminance.Last())
                    str += ",";
            }
            str += "]";

            return str;
        }
    }
}
