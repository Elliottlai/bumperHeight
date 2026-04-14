using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO.Ports;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Machine.Core
{
    /// <summary>
    /// 定義初始化Modbus序列埠元件(RS485)所需的參數群
    /// </summary>
    public class ModbusConnectConifgSerial : INotifyPropertyChanged
    {
        private string _portName = "COM1";
        private int _baudRate = 115200;
        private Parity _parity = Parity.None;
        private int _dataBits = 8;
        private StopBits _stopBits = StopBits.One;
        private int _receiveTimeout = 1000;
        private int _sendTimeout = 1000;
        private int _retryTimes = 10;

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public virtual string PortName
        {
            get { return _portName; }
            set
            {
                _portName = value;
                OnPropertyChanged("PortName");
            }
        }

        public virtual int BaudRate
        {
            get { return _baudRate; }
            set
            {
                _baudRate = value;
                OnPropertyChanged("BaudRate");
            }
        }

        public virtual Parity Parity
        {
            get { return _parity; }
            set
            {
                _parity = value;
                OnPropertyChanged("Parity");
            }
        }

        public virtual int DataBits
        {
            get { return _dataBits; }
            set
            {
                _dataBits = value;
                OnPropertyChanged("DataBits");
            }
        }

        public virtual StopBits StopBits
        {
            get { return _stopBits; }
            set
            {
                _stopBits = value;
                OnPropertyChanged("StopBits");
            }
        }
    }

    public class ModbusConnectConifgTcp : INotifyPropertyChanged
    {
        private string _ipAddress = "127.0.0.1";
        private int _port = 502;

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public string IpAddress
        {
            get { return _ipAddress; }
            set
            {
                if (string.IsNullOrEmpty(value))
                {
                    throw new ArgumentNullException();
                }
                IPAddress result;
                if (IPAddress.TryParse(value, out result))
                {
                    _ipAddress = value;
                }
                OnPropertyChanged("IpAddress");
            }
        }

        public int Port
        {
            get { return _port; }
            set
            {
                _port = value;
                OnPropertyChanged("Port");
            }
        }

    }
}
