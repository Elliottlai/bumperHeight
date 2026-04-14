using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Machine.Core
{
    /// <summary>
    /// Modbus-RTU用戶端主要功能實作
    /// </summary>
    public class ModbusClientRtu : ModbusClientBase
    {
        private readonly ushort MODBUS_DEFAULT_LENGTH = 2;
        private int retryInterval = 100;
        public SerialPort ModbusSerialPort { get; set; }

        public override bool Connect<T>(T ConnectConfig)
        {
            if (ConnectConfig == null)
            {
                throw new ArgumentNullException("ConnectConfig");
            }

            ModbusConnectConifgSerial connectConfig = ConnectConfig as ModbusConnectConifgSerial;
            if (connectConfig == null)
            {
                throw new NotSupportedException("This config is not Modbus-RTU style.");
            }
            

            if (this.ModbusSerialPort == null)
            {
                this.ModbusSerialPort = new SerialPort(connectConfig.PortName, connectConfig.BaudRate, connectConfig.Parity, connectConfig.DataBits, connectConfig.StopBits);
            }

            if (this.ModbusSerialPort.IsOpen)
            {
                this.ModbusSerialPort.Close();
                Thread.Sleep(100);
            }
            Base = new ModbusRtu(); //連結Modbus通訊協定

            this.ModbusSerialPort.Open();
            this.IsConnected = this.ModbusSerialPort.IsOpen;
            return this.IsConnected;
        }

        public override bool Disconnect()
        {
            if (!this.IsConnected)
            {
                return false;
            }
            this.ModbusSerialPort.Close();
            this.IsConnected = this.ModbusSerialPort.IsOpen;
            return !this.IsConnected;
        }

        public override byte[] Receive()
        {
            if (!this.IsConnected)
            {
                throw new ModbusException("No Connect");
            }
            var timeoutTemp = this.ModbusSerialPort.ReadTimeout;
            this.ModbusSerialPort.ReadTimeout = this.ReceiveTimeout;
            byte[] bufferArray = new byte[256];
            byte[] resultArray = null;
            int receiveLength = 0;  //已收到資料總長度
            var retryCount = 0;

            using (MemoryStream stream = new MemoryStream())
            {
                while (retryCount < this.RetryTimes)
                {
                    if (this.ModbusSerialPort.BytesToRead > 0)
                    {
                        var receiveCount = this.ModbusSerialPort.Read(bufferArray, 0, bufferArray.Length);
                        stream.Write(bufferArray, 0, receiveCount);
                        resultArray = stream.ToArray();
                        if (receiveCount <= 0)
                        {
                            break;
                        }
                        receiveLength += receiveCount;
                        retryCount = 0;
                    }
                    //檢查已收到的資料是否已足夠
                    if (receiveLength >= MODBUS_DEFAULT_LENGTH)
                    {
                        var length = stream.ToArray()[MODBUS_DEFAULT_LENGTH-1]; //第2byte為modbus報文長度紀錄

                        if (receiveLength >= MODBUS_DEFAULT_LENGTH + length)
                        {
                            break;  //已收完所需資料則跳離
                        }
                    }
                    retryCount++;
                    Thread.Sleep(retryInterval);
                    //空轉
                    //SpinWait.SpinUntil(() => retryCount > this.RetryTimes, this.ReceiveTimeout);
                }
            }

            if (resultArray == null || resultArray.Length == 0)
            {
                throw new ModbusException("Receive Timeout");
            }
            this.ModbusSerialPort.ReadTimeout = timeoutTemp;
            return resultArray;
        }

        public override bool Send(byte[] RequestArray)
        {
            this.ModbusSerialPort.Write(RequestArray, 0, RequestArray.Length);
            return true;
        }

        public override void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }
        ~ModbusClientRtu()
        {
            Dispose(false);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (this.Disposed)
                return;

            this.IsConnected = false;

            if (disposing)
            {
                //clean management resource
                if (this.ModbusSerialPort != null)
                {
                    this.ModbusSerialPort.Dispose();
                    this.ModbusSerialPort = null;
                }
            }

            //clean unmanagement resource

            //change flag
            this.Disposed = true;
        }
    }    
}
