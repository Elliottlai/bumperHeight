using Machine.Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Machine.Core
{
    /// <summary>
    /// Modbus用戶端主要功能的抽象類別
    /// </summary>
    public abstract class ModbusClientBase : IModbusTransport, IComponent, IDisposable
    {
        //fields
        private int _receiveTimeout = 1000;
        private int _sendTimeout = 1000;
        private bool _isConnected = false;
        private int _retryTimes = 10;

        public string UID { get; set; }
        public string Name { get; set; }

        //virtual properties
        public virtual bool IsConnected
        {
            get { return _isConnected; }
            set { _isConnected = value; }
        }

        public virtual int ReceiveTimeout
        {
            get { return _receiveTimeout; }
            set { _receiveTimeout = value; }
        }

        public virtual int SendTimeout
        {
            get { return _sendTimeout; }
            set { _sendTimeout = value; }
        }

        public virtual int RetryTimes
        {
            get { return _retryTimes; }
            set { _retryTimes = value; }
        }

        protected virtual bool Disposed
        {
            get;
            set;
        }

        /// <summary>
        /// 用於編譯Modbus報文的基礎模組
        /// </summary>
        internal ModbusBase Base { get; set; }

        //virtual method
        public virtual byte[] Conversation(byte[] RequestArray)
        {
            if (RequestArray == null)
            {
                throw new ArgumentNullException("RequestArray is null.");
            }
            Send(RequestArray);
            var resultArray = Receive();
            return resultArray;
        }

        //abstract method
        public abstract bool Disconnect();

        public abstract byte[] Receive();

        public abstract bool Send(byte[] RequestArray);

        public abstract bool Connect<T>(T ConnectConfig);

        public abstract void Dispose();

        public virtual byte[] ReadCoils(byte Unit, ushort StartAddress, ushort Quantity)
        {
            var requestArray = this.Base?.ReadCoils(Unit, StartAddress, Quantity);
            var responseArray = Conversation(requestArray);
            var result = this.Base?.GetResult(requestArray, responseArray);
            return result;
        }

        public virtual byte[] ReadDiscreteInputs(byte Unit, ushort StartAddress, ushort Quantity)
        {
            var requestArray = this.Base?.ReadDiscreteInputs(Unit, StartAddress, Quantity);
            var responseArray = Conversation(requestArray);
            var result = this.Base?.GetResult(requestArray, responseArray);
            return result;
        }

        public virtual byte[] ReadHoldingRegisters(byte Unit, ushort StartAddress, ushort Quantity)
        {
            var requestArray = this.Base?.ReadHoldingRegisters(Unit, StartAddress, Quantity);
            var responseArray = Conversation(requestArray);
            var result = this.Base?.GetResult(requestArray, responseArray);
            return result;
        }

        public virtual byte[] ReadInputRegisters(byte Unit, ushort StartAddress, ushort Quantity)
        {
            var requestArray = this.Base?.ReadInputRegisters(Unit, StartAddress, Quantity);
            var responseArray = Conversation(requestArray);
            var result = this.Base?.GetResult(requestArray, responseArray);
            return result;
        }

        public virtual bool WriteSingleCoil(byte Unit, ushort OutputAddress, bool OutputValue)
        {
            var requestArray = this.Base?.WriteSingleCoil(Unit, OutputAddress, OutputValue);
            var responseArray = Conversation(requestArray);
            var result = this.Base?.GetResult(requestArray, responseArray);
            return result != null;
        }

        public virtual bool WriteSingleRegister(byte Unit, ushort OutputAddress, short OutputValue)
        {
            var requestArray = this.Base?.WriteSingleRegister(Unit, OutputAddress, OutputValue);
            var responseArray = Conversation(requestArray);
            var result = this.Base?.GetResult(requestArray, responseArray);
            return result != null;
        }

        public virtual bool WriteMultipleCoils(byte Unit, ushort StartAddress, ushort Quantity, byte[] OutputValues)
        {
            var requestArray = this.Base?.WriteMultipleCoils(Unit, StartAddress, Quantity, OutputValues);
            var responseArray = Conversation(requestArray);
            var result = this.Base?.GetResult(requestArray, responseArray);
            return result != null;
        }

        public virtual bool WriteMultipleRegisters(byte Unit, ushort StartAddress, ushort Quantity, short[] OutputValues)
        {
            var requestArray = this.Base?.WriteMultipleRegisters(Unit, StartAddress, Quantity, OutputValues);
            var responseArray = Conversation(requestArray);

            var result = this.Base?.GetResult(requestArray, responseArray);
            return result != null;
        }                
    }
}
