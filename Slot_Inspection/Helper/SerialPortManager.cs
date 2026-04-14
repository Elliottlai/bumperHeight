using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Helpers
{

    public static class SerialPortManager
    {


        private static SerialPort _serialPort;
        private static int _baudRate = 38400;
        private static int _dataBits = 8;
        private static Handshake _handshake = Handshake.None;
        private static Parity _parity = Parity.None;
        private static string _portName;
        private static StopBits _stopBits = StopBits.One;


        private class ManagedPort
        {
            public SerialPort Port;
            public Action<string> OnDataReceived;
        }

        private static readonly ConcurrentDictionary<string, Lazy<ManagedPort>> _ports = new ConcurrentDictionary<string, Lazy<ManagedPort>>();

        public static void Register(string portName, int baudRate = 9600, Action<string> onDataReceived = null)
        {
            _ports.GetOrAdd(portName, key => new Lazy<ManagedPort>(() =>
            {
                var sp = new SerialPort(portName, baudRate)
                {
                    NewLine = "\n",
                    DtrEnable = false,
                    RtsEnable = false,
                    ReadTimeout = 500
                };

                var mp = new ManagedPort
                {
                    Port = sp,
                    OnDataReceived = onDataReceived
                };

                sp.DataReceived += (_, __) =>
                {
                    try
                    {
                        string data = sp.ReadExisting();
                        if (!string.IsNullOrEmpty(data))
                        {
                            mp.OnDataReceived?.Invoke(data); // 呼叫外部註冊的處理器
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"讀取 {portName} 錯誤: {ex.Message}");
                    }
                };

                sp.Open();
                return mp;
            }));
        }
        static object locked = new object();
        public static void Write(string portName, string data)
        {
            lock(locked)
            {
                if (_ports.TryGetValue(portName, out var lazy) && lazy.Value.Port.IsOpen)
                {
                    lazy.Value.Port.Write(data);
                }
                Thread.Sleep(500);
            }

        }

        public static void Close(string portName)
        {
            if (_ports.TryRemove(portName, out var lazy))
            {
                var sp = lazy.Value.Port;
                if (sp.IsOpen)
                    sp.Close();
            }
        }

        public static void CloseAll()
        {
            foreach (var kv in _ports)
            {
                if (kv.Value.IsValueCreated && kv.Value.Value.Port.IsOpen)
                {
                    kv.Value.Value.Port.Close();
                }
            }
            _ports.Clear();
        }




    }
}
