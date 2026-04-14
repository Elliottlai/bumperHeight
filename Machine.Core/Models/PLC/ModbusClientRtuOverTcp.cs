using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Machine.Core
{
    /// <summary>
    /// Modbus-RTU報文並使用TCP傳輸的用戶端模式
    /// </summary>
    public class ModbusClientRtuOverTcp : ModbusClientBase
    {
        private Socket ModbusSocket { get; set; }
        private IPEndPoint ModbusEndPoint { get; set; }
        public override bool Connect<T>(T ConnectConfig)
        {
            if (ConnectConfig == null)
            {
                throw new ArgumentNullException("Config is null.");
            }            

            ModbusConnectConifgTcp connectConfig = ConnectConfig as ModbusConnectConifgTcp;
            if (connectConfig == null)
            {
                throw new NotSupportedException("This config is not TCP style.");
            }            

            if (this.ModbusSocket == null)
            {
                this.ModbusSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            }
            Base = new ModbusRtu(); //連結Modbus-Rtu通訊協定模組

            this.ModbusEndPoint = new IPEndPoint(IPAddress.Parse(connectConfig.IpAddress), connectConfig.Port);
            this.ModbusSocket.Connect((EndPoint)ModbusEndPoint);
            this.IsConnected = this.ModbusSocket.Connected;
            return this.IsConnected;
        }

        public override bool Disconnect()
        {
            if (!this.IsConnected)
            {
                return false;
            }

            this.ModbusSocket.Shutdown(SocketShutdown.Both);
            this.ModbusSocket.Disconnect(false);
            this.IsConnected = this.ModbusSocket.Connected;
            return !this.IsConnected;
        }

        public override byte[] Receive()
        {
            if (!this.connectVerify())
            {
                return null;
            }

            var tempTimeOut = this.ModbusSocket.ReceiveTimeout;
            this.ModbusSocket.ReceiveTimeout = this.ReceiveTimeout;

            var bufferArray = new byte[256];
            var socketError = new SocketError();
            byte[] responseArray = null;

            var retryCount = 0;
            Stopwatch sw = new Stopwatch();
            using (MemoryStream memory = new MemoryStream())
            {
                while (retryCount < this.RetryTimes)
                {
                    if (this.ModbusSocket.Available > 0)
                    {
                        var receiveCount = this.ModbusSocket.Receive(bufferArray, 0, bufferArray.Length, SocketFlags.None, out socketError);
                        if (receiveCount == 0 || socketError != SocketError.Success)
                        {
                            break;
                        }
                        memory.Write(bufferArray, 0, receiveCount);
                        sw.Restart();
                        retryCount = 0;
                    }

                    retryCount++;
                    Thread.Sleep(100);
                    //空轉
                    //if(SpinWait.SpinUntil(() => retryCount > this.RetryTimes, this.ReceiveTimeout))
                    //{
                    //    break;
                    //}
                }

                this.ModbusSocket.ReceiveTimeout = tempTimeOut;
                responseArray = memory.ToArray();
            }

            if (responseArray == null || responseArray.Length == 0)
            {
                throw new ModbusException("Receive reponse timeout");
            }
            return responseArray;
        }

        public override bool Send(byte[] RequestArray)
        {
            if (!this.connectVerify())
            {
                return false;
            }
            this.ModbusSocket.Send(RequestArray);
            return true;
        }

        public override void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }
        ~ModbusClientRtuOverTcp()
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
                if (this.ModbusSocket != null)
                {
                    this.ModbusSocket.Shutdown(SocketShutdown.Both);
                    this.ModbusSocket.Disconnect(false);
                    this.ModbusSocket.Dispose();
                    this.ModbusSocket = null;
                }
            }

            //clean unmanagement resource

            //change flag
            this.Disposed = true;
        }

        private bool connectVerify()
        {
            if (!this.IsConnected || this.Disposed)
            {
                return false;
            }
            return true;
        }

    }    
}
