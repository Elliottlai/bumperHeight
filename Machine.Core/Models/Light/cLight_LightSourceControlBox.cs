using Machine.Core.Interfaces;
using SynPower.Lights.Litsource;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Machine.Core.Enums;
using System.IO.Ports;
using System.Threading;

namespace Machine.Core
{
    public class cLight_LightSourceControlBox : ILightController, IDisposable
    {
        //  Gd2045a1 Light = null;


        public string UID { get; set; }

        public string Name { get; set; }

        public LightType Type => LightType.LightSource;

        public int Channel { get; set; }

        public string Network_IPAddress { get; set; }

        public int Network_Port { get; set; }

        public int Network_Timeout { get; set; }
        public int MaxLevel { get; set; } = 255;
        public string PortName
        {
            get => Comport?.PortName ?? string.Empty;
            set => Comport = SerialPortManager.GetSerialPort(value);
        }

        public int BaudRate { get; set; }

        public Parity Parity { get; set; }

        public int DataBits { get; set; }

        public StopBits StopBits { get; set; }

        public int ReadTimeout { get; set; }

        public int WriteTimeout { get; set; }

        protected SerialPort Comport { set; get; }
        private int[] ChannelsID;

        public cLight_LightSourceControlBox(string portName, ISerialPortArgs args, int channelCount)
        {

            try
            {

                BaudRate = args.BaudRate;
                Parity = args.Parity;
                DataBits = args.DataBits;
                StopBits = args.StopBits;
                ReadTimeout = args.ReadTimeout;
                WriteTimeout = args.WriteTimeout;

                PortName = portName;

                Comport.BaudRate = args.BaudRate;
                Comport.Parity = args.Parity;
                Comport.DataBits = args.DataBits;
                Comport.StopBits = args.StopBits;
                Comport.ReadTimeout = args.ReadTimeout;
                Comport.WriteTimeout = args.WriteTimeout;




                Luminance = new int[channelCount];

                ChannelsID = new int[channelCount];
                for (byte i = 0; i < channelCount; i++)
                    ChannelsID[i] = i + 1;
            }
            catch (Exception ex)
            {
                throw new Exception($"燈源初始化失敗：{ex.Message}");
            }
        }

        //public cLight_LightSource(Gd2045a1 light)
        //{
        //    Light = light;
        //}

        public void Initialize()
        {
            // Light = new Gd2045a1("COM" + m_Set_sp.No.ToString () , m_Set_sp.BaudRate);
            // Light.Initialize();
        }
        byte[] aryResult;
        private int delayTime = 60;
        private int retryTimes = 3;
        private int[] Luminance;
        // private int[] luminances;
        public int GetLuminance() { return 0; }
        // public bool SetLuminance(byte value, bool Wait = true)
        // {
        //if (!this.IsEnable) return;

        //int excutingCount = 0;

        //do
        //{
        //    try
        //    {
        //        this.ThrowIfChannelOutOfRange(Channel);
        //        this.ThrowIfLuminanceOutOfRange(value);

        //        //var getCmd = new GetBrightnessCommand();
        //        //var lums = this.ExcuteCommand(getCmd);
        //        //lums[channel] = (byte)value;
        //        this.Luminance[Channel] = (byte)value;


        //        var setCmd = new SetBrightnessCommand((byte)this.Luminance[0], (byte)this.Luminance[1], (byte)this.Luminance[2], (byte)this.Luminance[3]);
        //        //this.WriteCommand(setCmd);
        //        var result = this.ExcuteCommand(setCmd);

        //        if (result.First() != Command.ACK)
        //            throw new InvalidOperationException("Failed to SetLuminance.");

        //        break;
        //    }
        //    catch (Exception ex)
        //    {
        //        excutingCount++;

        //        if (excutingCount > this.retryTimes) throw ex;

        //        //this.comport.Close();

        //        //Thread.Sleep(this.delayTime);

        //        continue;
        //    }

        //} while (true);
        //Luminance = value;
        //     return true;
        // }

        private void ThrowIfChannelOutOfRange(int channel)
        {
            if (channel < 0 || channel > 3)
                throw new ArgumentOutOfRangeException("channel");
        }

        private void ThrowIfLuminanceOutOfRange(int value)
        {

            if (value < 0)
                value = 0;
            if (value > 100)
            {
                value = 0;
            }
            //   throw new ArgumentOutOfRangeException("vlaue");
        }

        public byte[] ExcuteCommand(Command cmd)
        {
            try
            {
                if (!this.Comport.IsOpen) this.Comport.Open();

                this.Comport.DiscardInBuffer();
                this.Comport.DiscardOutBuffer();

                Thread.Sleep(this.delayTime);

                this.Comport.Write(cmd, 0, cmd.CommandLength);

                Thread.Sleep(this.delayTime);

                aryResult = new byte[cmd.ResponseLength];
                this.Comport.Read(aryResult, 0, cmd.ResponseLength);

                return aryResult
                    .Skip(cmd.ResponseHeadLength)
                    .Take(cmd.ResponseDataTextLength)
                    .ToArray();
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        public int GetChannelCount()
        {
            return ChannelsID.Length;
        }

        public void SetLuminance(int channel, int value)
        {
            this.ThrowIfLuminanceOutOfRange(value);

            int[] v = new int[Luminance.Count()];

            for (int i = 0; i < Luminance.Count(); i++)
            {
                v[i] = Luminance[i];

            }
            v[channel - 1] = value;
            SetLuminance(v);


            //Thread.Sleep(100);
            Luminance[channel - 1] = value;


        }

        public void SetLuminance(int[] value, bool changeOnly = true)
        {

            int excutingCount = 0;

            do
            {
                try
                {
                    this.ThrowIfChannelOutOfRange(Channel);


                    for (int i = 0; i < value.Count(); i++)

                        this.ThrowIfLuminanceOutOfRange(value[i]);


                    //var getCmd = new GetBrightnessCommand();
                    //var lums = this.ExcuteCommand(getCmd);
                    //lums[channel] = (byte)value;
                    bool same = true;
                    for (int i = 0; i < value.Count(); i++)
                    {
                        same = same & this.Luminance[i] == value[i];
                        this.Luminance[i] = value[i];

                    }
                    if (same)
                        return;
                    var setCmd = new SetBrightnessCommand((byte)this.Luminance[0], (byte)this.Luminance[1], (byte)this.Luminance[2], (byte)this.Luminance[3]);
                    //this.WriteCommand(setCmd);
                    var result = this.ExcuteCommand(setCmd);

                    if (result.First() != Command.ACK)
                        throw new InvalidOperationException("Failed to SetLuminance.");

                    break;
                }
                catch (Exception ex)
                {
                    excutingCount++;

                    if (excutingCount > this.retryTimes) throw ex;

                    //this.comport.Close();

                    //Thread.Sleep(this.delayTime);

                    continue;
                }

            } while (true);
            Luminance = value;
        }

        public int GetLuminance(int channel)
        {
            return Luminance[channel - 1];
        }

        int[] ILightController.GetLuminance()
        {
            return Luminance;
        }

        public void Dispose()
        {
            SetLuminance(new int[GetChannelCount()], false);
            this.Comport.Dispose();
        }
    }
}
