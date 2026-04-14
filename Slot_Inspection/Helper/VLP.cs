using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace LightControl
{

    public class VLP
    {
        public delegate void valLogEvent(Object sender, bool isTx, bool isRx, bool isCRCOK, string log);
        public event valLogEvent ValLogDisplay;

        public VLP()
        {
            Console.WriteLine("Base construct");
        }

        public virtual void Connect(string address, int? port)
        {
            PrintLog("Create connection to " + address);
        }

        public virtual void Close()
        {
            PrintLog("Terminate connection");
        }

        public virtual string SendCommand(string command, bool isAutoChecksum)
        {
            StringBuilder finalCommand = new StringBuilder();

            finalCommand.Append(command);
            if (isAutoChecksum)
            {
                finalCommand.Append(Checksum(command));
            }
            finalCommand.Append("\r\n");

            ValLogDisplay?.Invoke(this, true, false, false, finalCommand.ToString());

            return finalCommand.ToString();
        }

        public string parse_RxString(string rx, bool fixID)
        {
            StringBuilder interpreted = new StringBuilder("");

            string pattern = @"([FLON])([0-9]*)";
            string logChecksum = string.Empty;

            string dataPart = rx.Substring(0, rx.Length - 4);
            string checksumPart = rx.Substring(rx.Length - 4, 2);
            string chanelID = dataPart.Substring(1, 2);

            string payload = string.Empty;
            if (fixID)
            {
                payload = dataPart.Substring(3, dataPart.Length - 5);
            }
            else
            {
                payload = dataPart.Substring(3);
            }

            interpreted.Append("CH ");
            try
            {
                interpreted.Append((int.Parse(chanelID, System.Globalization.NumberStyles.HexNumber) + 1) + "");
            }
            catch (Exception exp)
            {
                interpreted.Append("undefined");
            }


            Match match = Regex.Match(payload, pattern, RegexOptions.None);

            while (match.Success)
            {
                String opcode = match.Groups[1].Value;
                String data = match.Groups[2].Value;

                interpreted.Append("\t");

                switch (opcode)
                {
                    case "O":
                        interpreted.Append("OK");
                        break;
                    case "N":
                        switch (data)
                        {
                            case "01":
                                interpreted.Append("CommandError");
                                break;
                            case "02":
                                interpreted.Append("ChecksumError");
                                break;
                            case "03":
                                interpreted.Append("ValueOutOfRange");
                                break;
                            default:
                                interpreted.Append("UnknownError");
                                break;
                        }
                        break;
                    case "F":
                        interpreted.Append("Brightness:");
                        interpreted.Append(data);
                        break;
                    case "L":
                        interpreted.Append("ON/OFF:");
                        switch (data)
                        {
                            case "1":
                                interpreted.Append("ON");
                                break;
                            case "0":
                                interpreted.Append("OFF");
                                break;

                            default:
                                interpreted.Append("UnknownStatus");
                                break;
                        }
                        break;
                    default:
                        interpreted.Append("_UnknownCode(" + opcode + ")");
                        break;
                }
                match = match.NextMatch();
            }


            return interpreted.ToString();
        }

        public void Set_Light_Intensity(uint channel, uint intensity, uint? id)
        {
            StringBuilder command = new StringBuilder();
            command.Clear();

            command.Append("@");
            command.Append(channel.ToString("X2"));
            command.Append("F");
            command.Append(intensity.ToString("D3"));
            if (id.HasValue)
            {
                command.Append(id.Value.ToString("X2"));
            }

            SendCommand(command.ToString(), true);
        }

        public void Set_Light_Mode(uint channel, uint mode, uint? id)
        {
            StringBuilder command = new StringBuilder();
            command.Clear();

            command.Append("@");
            command.Append(channel.ToString("X2"));
            command.Append("S");
            command.Append(mode.ToString("D2"));
            if (id.HasValue)
            {
                command.Append(id.Value.ToString("X2"));
            }

            SendCommand(command.ToString(), true);
        }

        public void Set_Light_OnOff(uint channel, uint mode, uint? id)
        {
            StringBuilder command = new StringBuilder();
            command.Clear();

            command.Append("@");
            command.Append(channel.ToString("X2"));
            command.Append("L");
            command.Append(mode.ToString("D1"));
            if (id.HasValue)
            {
                command.Append(id.Value.ToString("X2"));
            }

            SendCommand(command.ToString(), true);
        }

        public void Set_changableIP(string ip)
        {
            StringBuilder command = new StringBuilder();
            command.Clear();

            command.Append("@");
            command.Append("00");
            command.Append("E01");
            command.Append(ip.Trim());
            command.Append(".");

            SendCommand(command.ToString(), true);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="channel"></param>
        /// <param name="id">0</param>
        public void Check_Light_SettingStatus(uint channel, uint? id)
        {
            StringBuilder command = new StringBuilder();
            command.Clear();

            command.Append("@");
            command.Append(channel.ToString("X2"));
            command.Append("M");
            if (id.HasValue)
            {
                command.Append(id.Value.ToString("X2"));
            }

            SendCommand(command.ToString(), true);
        }


        public void Check_Light_Overcurrent(uint channel, uint? id)
        {
            StringBuilder command = new StringBuilder();
            command.Clear();

            command.Append("@");
            command.Append(channel.ToString("X2"));
            command.Append("C");
            if (id.HasValue)
            {
                command.Append(id.Value.ToString("X2"));
            }

            SendCommand(command.ToString(), true);
        }

        public void Reset_Light(uint channel, uint? id)
        {
            StringBuilder command = new StringBuilder();
            command.Clear();

            command.Append("@");
            command.Append(channel.ToString("X2"));
            command.Append("R");
            if (id.HasValue)
            {
                command.Append(id.Value.ToString("X2"));
            }

            SendCommand(command.ToString(), true);
        }

        public string Checksum(string input)
        {
            StringBuilder finalCommand = new StringBuilder();

            // Calculate Checksum
            byte sum = 0;
            byte[] bytes = Encoding.ASCII.GetBytes(input);
            foreach (byte b in bytes)
            {
                sum += b;
            }

            finalCommand.Append(sum.ToString("X2"));

            return finalCommand.ToString();
        }

        protected void Status_Parser(string status, bool isRequireID)
        {
            if (status.Length < 6)
                return;
            string pattern = String.Empty;
            string logChecksum = string.Empty;
            string dataPart = status.Substring(0, status.Length - 4);
            string checksumPart = status.Substring(status.Length - 4, 2);

            string checksumCal = Checksum(dataPart);


            //if (isRequireID)
            //{
            //    pattern = @"(@([0-9A-F]{2})([FSLMCRON])(.+)?([0-9A-F]{2}))([0-9A-F]{2})\r\n";
            //}
            //else
            //{
            //   pattern = @"(@([0-9A-F]{2})([FSLMCRON])(.+)?)([0-9A-F]{2})\r\n";
            //}

            //Match match = Regex.Match(status, pattern, RegexOptions.None);

            //ValLogDisplay?.Invoke(this, false, false, false, "Rx: " + status );

            //if (match.Success == true)
            //{

            //string checksum = Checksum(match.Groups[1].Value);

            //_logChannel = match.Groups[2].Value;
            //_logResult = match.Groups[3].Value;
            //_logPayload = match.Groups[4].Success ? match.Groups[4].Value : "";
            //if (isRequireID)
            //{
            //_logId = match.Groups[5].Success ? match.Groups[5].Value : "";
            //    logChecksum = match.Groups[6].Value;
            //}
            //else
            //{
            //_logId = "NOT SUPPORTED";
            //   logChecksum = match.Groups[5].Value;
            //

            if (checksumPart.Equals(checksumCal) == true)
            {
                //ValLogDisplay?.Invoke(this, false, false, true, "Received data is correct");
                ValLogDisplay?.Invoke(this, false, true, true, status);
            }
            else
            {
                //ValLogDisplay?.Invoke(this, false, false, true, "Checkum is incorect: Expected value = " + checksum + ", Actual value = " + _logChecksum);
                ValLogDisplay?.Invoke(this, false, true, false, status);
            }
            // }
            //else
            //{
            // ValLogDisplay?.Invoke(this, false, false, false, "Pattern of received data is incorect");
            //}
        }

        protected void PrintLog(string log)
        {
            ValLogDisplay?.Invoke(this, false, false, false, log);
        }

    }

    public class VLP_Serial : VLP
    {

        private SerialPort _serialPort;
        private int _baudRate = 115200;
        private int _dataBits = 8;
        private Handshake _handshake = Handshake.None;
        private Parity _parity = Parity.None;
        private string _portName;
        private StopBits _stopBits = StopBits.One;

        private string bufferString = string.Empty;

        public int BaudRate { get { return _baudRate; } set { _baudRate = value; } }
        public int DataBits { get { return _dataBits; } set { _dataBits = value; } }
        public Handshake Handshake { get { return _handshake; } set { _handshake = value; } }
        public Parity Parity { get { return _parity; } set { _parity = value; } }
        public string PortName { get { return _portName; } set { _portName = value; } }


        public VLP_Serial()
        {
            // Do additional construction
        }

        public override void Connect(string address, int? port)
        {
            _portName = address;
            try
            {

                 Helpers.SerialPortManager.Register (_portName, _baudRate, data => {

 
                     //Check if string contains the terminator  
                     if (bufferString.IndexOf("\r\n") > -1)
                     {
                         //If bufferString does contain terminator we cannot assume that it is the last character received 
                         string workingString = bufferString.Substring(0, bufferString.IndexOf("\r\n") + 2);
                         //Remove the data up to the terminator from bufferString 
                         bufferString = bufferString.Substring(bufferString.IndexOf("\r\n") + 2);

                         //Do something with workingString 
                         Status_Parser(workingString, true);
                         bufferString = string.Empty;
                     }



                 });
                //_serialPort = new SerialPort
                //{
                //    BaudRate = _baudRate,
                //    DataBits = _dataBits,
                //    Handshake = _handshake,
                //    Parity = _parity,
                //    PortName = _portName,
                //    StopBits = _stopBits
                //}; 
                //if (!_serialPort.IsOpen)
                //    _serialPort.Open();

                PrintLog("Connection to [" + _portName + "] has been established");
            }
            catch (Exception exp)
            {
                throw exp; //PrintLog(exp.Message);
            }
        }

        public override void Close()
        {
            try
            {

                Helpers.SerialPortManager.Close(_portName);
               //_serialPort.Close();
               // _serialPort.Dispose();

                PrintLog("Connection to [" + _portName + "] has been terminated");
            }
            catch (Exception exp)
            {
                throw exp;
                //PrintLog(exp.Message);
            }
        }

        public override string SendCommand(string command, bool isAutoChecksum)
        {

            string data = base.SendCommand(command, isAutoChecksum);

            Helpers.SerialPortManager.Write(_portName, data);
            //_serialPort.Write(data);

            return "";
        }
        static object lockPort = new object();

        private void DataReceived(object sender, SerialDataReceivedEventArgs e)
        {

            //Initialize a buffer to hold the received data 
            byte[] buffer = new byte[_serialPort.ReadBufferSize];

            //There is no accurate method for checking how many bytes are read 
            //unless you check the return from the Read method 
            int bytesRead = _serialPort.Read(buffer, 0, buffer.Length);

            //For the example assume the data we are received is ASCII data. 
            bufferString += Encoding.ASCII.GetString(buffer, 0, bytesRead);
            //Check if string contains the terminator  
            if (bufferString.IndexOf("\r\n") > -1)
            {
                //If bufferString does contain terminator we cannot assume that it is the last character received 
                string workingString = bufferString.Substring(0, bufferString.IndexOf("\r\n") + 2);
                //Remove the data up to the terminator from bufferString 
                bufferString = bufferString.Substring(bufferString.IndexOf("\r\n") + 2);

                //Do something with workingString 
                Status_Parser(workingString, true);
                bufferString = string.Empty;
            }

        }
    }


    public class VLP_Ethernet : VLP
    {
        private TcpClient _tcpClient;
        private int _port;
        private string _address;

        private NetworkStream _stream;
        private Thread _rxThread;

        private int _rxTime;
        private bool _rxStop;

        public int Port { get { return _port; } }
        public string Address { get { return _address; } }
        public int RxTime { get { return _rxTime; } set { _rxTime = RxTime; } }



        public VLP_Ethernet()
        {
            // Do additional construction
            _rxTime = 300;
            _rxStop = false;
        }

        public override void Connect(string address, int? port)
        {
            try
            {
                _address = address;
                _port = port ?? 0;
                _tcpClient = new TcpClient();
                _tcpClient.Connect(_address, _port);
                _stream = _tcpClient.GetStream();

                PrintLog("Connection to [" + _address + ":" + _port + "] has been established");

                _rxThread = new Thread(ExecuteInForeground);
                _rxThread.Start(this);
                Thread.Sleep(1000);
            }
            catch (Exception exp)
            {
                //PrintLog(exp.ToString());
                throw exp;
            }
        }

        public override void Close()
        {
            try
            {
                _rxStop = true;
                _tcpClient.Close();
                _stream.Close();
                _stream.Dispose();

                //write log
                //PrintLog("Ethernet Connection has been terminated");
            }
            catch (Exception exp)
            {
                throw exp;
                //PrintLog(exp.Message);
            }
        }

        public override string SendCommand(string command, bool isAutoChecksum)
        {
            string data = base.SendCommand(command, isAutoChecksum);

            Byte[] dataB = System.Text.Encoding.ASCII.GetBytes(data);
            _stream.Write(dataB, 0, dataB.Length);
            return "";
        }

        private void ExecuteInForeground(Object obj)
        {
            byte[] data = new Byte[256];
            string responseData;
            do
            {
                if (_stream.DataAvailable)
                {
                    Int32 bytes = _stream.Read(data, 0, data.Length);
                    responseData = System.Text.Encoding.ASCII.GetString(data, 0, bytes);

                    Status_Parser(responseData, false);
                }
                Thread.Sleep(_rxTime);
            } while (_rxStop == false);
        }
    }
}
