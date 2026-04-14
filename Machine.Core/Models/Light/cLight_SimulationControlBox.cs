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
    public class cLight_SimulationControlBox : ILightController, IDisposable
    {
        public string UID { get; set; }

        public string Name { get; set; }

        public LightType Type => LightType.Simulation;

        #region NotUse
        [Obsolete("於光控器模組內無效", false)]
        public int Channel { get; set; }        

        public string Network_IPAddress { get; set; } = "10.0.0.10";
        public int Network_Port { get; set; } = 2000;
        public int Network_Timeout { get; set; }
        #endregion

        //public bool IsEnable { get; set; }
        public string PortName { get; set; }

        public int BaudRate { get; set; }
        public Parity Parity { get; set; }
        public int DataBits { get; set; }
        public StopBits StopBits { get; set; }
        public int ReadTimeout { get; set; }
        public int WriteTimeout { get; set; }

        protected SerialPort Comport { set; get; }
        public int MaxLevel { get; set; } = 255;

        private int[] ChannelsID;
        private int[] Luminance;

        public cLight_SimulationControlBox(string portName, int channelCount)
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
                throw new Exception($"燈源({portName})初始化失敗：{ex.Message}");
            }           
        }
        public cLight_SimulationControlBox(string portName, ISerialPortArgs args, int channelCount)
            : this(portName, channelCount)
        {
            BaudRate = args.BaudRate;
            Parity = args.Parity;
            DataBits = args.DataBits;
            StopBits = args.StopBits;
            ReadTimeout = args.ReadTimeout;
            WriteTimeout = args.WriteTimeout;
        }
        public int GetChannelCount() => Luminance.Count();

        public async void SetLuminance(int channel, int value)
        {
            Luminance[channel] = Convert.ToByte(value);
            await Task.Delay(100);
        }

        public async void SetLuminance(int[] ranks, bool changeOnly = true)
        {
            Luminance = ranks;
            await Task.Delay(100);
        }

        public int GetLuminance(int channel)
            => Luminance[channel];


        public int[] GetLuminance()
            => Luminance.ToArray();

        public void TurnOff()
        {            
            SetLuminance(new int[GetChannelCount()], false);            
        }
        private void ThrowIfLuminanceOutOfRange(int value)
        {
            if (value < 0 || value > MaxLevel)
                throw new ArgumentOutOfRangeException($"光源亮度設定值超出範圍(0~{MaxLevel}");
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
