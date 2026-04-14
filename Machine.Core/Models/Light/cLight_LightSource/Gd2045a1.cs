using Machine.Core.Interfaces;
using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SynPower.Lights.Litsource
{
    public class Gd2045a1 : ILight, IDisposable
    {
        private byte[] luminances;
        private SerialPort comport;
        private bool isDisposed;
        private int delayTime = 60;
        private int retryTimes = 3;

        private GetBrightnessCommand cmdGetBrightness = new GetBrightnessCommand();

        public Gd2045a1(string portName, int baudRate = 115200)
        {
            this.comport = new SerialPort(portName);
            this.comport.BaudRate = baudRate;
            this.comport.ReadTimeout = 2000;
            this.comport.WriteTimeout = 500;

            this.IsEnable = true;
        }

        public bool IsEnable
        {
            get;
            set;
        }

        ~Gd2045a1()
        {
            this.Dispose(false);
        }

        /// <summary>
        /// 取得或設定命令執行失敗後重新執行命令的次數，重新執行時會將SerialPort連線關閉並重新啟動。
        /// </summary>
        public int CommandRetryTimes
        {
            get { return this.retryTimes; }
            set { this.retryTimes = value; }
        }

        /// <summary>
        /// 取得或設定SerialPort的讀取作業逾時的毫秒數。
        /// </summary>
        public int ReadTimeout
        {
            get { return this.comport.ReadTimeout; }
            set { this.comport.ReadTimeout = value; }
        }

        /// <summary>
        /// 取得或設定SerialPort的寫入作業逾時的毫秒數。
        /// </summary>
        public int WriteTimeout
        {
            get { return this.comport.WriteTimeout; }
            set { this.comport.WriteTimeout = value; }
        }


        /// <summary>
        /// 取得或設定每個指令執行後的延遲等待的毫秒數。
        /// </summary>
        public int WriteDelayTime
        {
            get { return this.delayTime; }
            set { this.delayTime = value; }
        }

        /// <summary>
        /// 設定通道上的亮度值。
        /// </summary>
        /// <param name="channel">調光器上的通道編號，範圍為 0 ~ 3，共4個通道。</param>
        /// <param name="value">亮度值，範圍為 0 ~ 100。</param>
        [MethodImpl(MethodImplOptions.Synchronized)]
        public void SetLuminance(int channel, int value)
        {
            if (!this.IsEnable) return;

            int excutingCount = 0;

            do
            {
                try
                {
                    this.ThrowIfChannelOutOfRange(channel);
                    this.ThrowIfLuminanceOutOfRange(value);

                    //var getCmd = new GetBrightnessCommand();
                    //var lums = this.ExcuteCommand(getCmd);
                    //lums[channel] = (byte)value;
                    this.luminances[channel] = (byte)value;


                    var setCmd = new SetBrightnessCommand(this.luminances[0], this.luminances[1], this.luminances[2], this.luminances[3]);
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
        }

        /// <summary>
        /// 取得調光器通道上的亮度值。
        /// </summary>
        /// <param name="channel">調光器上的通道編號，範圍為 0 ~ 3，共4個通道。</param>
        /// <returns>亮度值。</returns>
        public int GetLuminance(int channel)
        {
            if (!this.IsEnable) return default(int);

            this.ThrowIfChannelOutOfRange(channel);

            var cmd = new GetBrightnessCommand();
            return this.ExcuteCommand(cmd)[channel];
        }

        public void WriteCommand(Command cmd)
        {
            if (!this.comport.IsOpen) this.comport.Open();

            this.comport.DiscardInBuffer();
            this.comport.DiscardOutBuffer();

            Thread.Sleep(this.delayTime);

            this.comport.Write(cmd, 0, cmd.CommandLength);

            Thread.Sleep(this.delayTime);
        }

        byte[] aryResult;
        public byte[] ExcuteCommand(Command cmd)
        {
            try
            {
                if (!this.comport.IsOpen) this.comport.Open();

                this.comport.DiscardInBuffer();
                this.comport.DiscardOutBuffer();

                Thread.Sleep(this.delayTime);

                this.comport.Write(cmd, 0, cmd.CommandLength);

                Thread.Sleep(this.delayTime);

                aryResult = new byte[cmd.ResponseLength];
                this.comport.Read(aryResult, 0, cmd.ResponseLength);

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

        public void Dispose()
        {
            if (!this.IsEnable) return;
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (this.isDisposed) return;
            if (!this.IsEnable) return;
            this.comport.Dispose();

            this.isDisposed = true;
        }

        private void ThrowIfChannelOutOfRange(int channel)
        {
            if (channel < 0 || channel > 3)
                throw new ArgumentOutOfRangeException("channel");
        }

        private void ThrowIfLuminanceOutOfRange(int value)
        {
            if (value < 0 || value > 100)
                throw new ArgumentOutOfRangeException("vlaue");
        }

        public void Initialize()
        {
            this.luminances = this.ExcuteCommand(this.cmdGetBrightness);
            this.SetLuminance(new int[] { 0, 1, 2, 3 }, new int[] { 0, 0, 0, 0 });
        }

        public void SetLuminance(int[] channels, int[] ranks)
        {
            if (channels.Count() != ranks.Count()) throw new InvalidOperationException("Channels and Ranks is difference Count");

            foreach (var channel in channels) this.ThrowIfChannelOutOfRange(channel);
            foreach (var rank in ranks) this.ThrowIfLuminanceOutOfRange(rank);

            int count = channels.Count();

            for (int index = 0; index < count; index++)
                this.SetLuminance(channels[index], ranks[index]);
        }
    }
}
