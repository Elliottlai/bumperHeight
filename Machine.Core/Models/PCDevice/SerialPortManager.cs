using Machine.Core.Interfaces;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Machine.Core
{
    public static class SerialPortManager
    {
        private static ConcurrentDictionary<string, SerialPort> Pool = new ConcurrentDictionary<string, SerialPort>();
        private static ConcurrentDictionary<string, object> Locks = new ConcurrentDictionary<string, object>();

        public static SerialPort GetSerialPort(string PortName)
        {
            Init();

            if (Pool.ContainsKey(PortName))
            {

                Pool.TryGetValue(PortName, out SerialPort Result);

                return Result;
            }

            return Register(PortName);
        }

        public static void Register(this SerialPort This)
        {

            try
            {
                if (!This.IsOpen)
                    This.Open();
                Pool.AddOrUpdate(This.PortName, This, (s, v) => v);
                Locks.AddOrUpdate(This.PortName, new object(), (s, v) => v);
            }
            catch (Exception ex)
            { 
            
            }

        }
        public static SerialPort Register(string Name)
        {
            SerialPort Result = new SerialPort(Name);
            Result.Register();
            return Result;
        }

        private static PropertyInfo[] SerialPortArgs = typeof(ISerialPortArgs).GetProperties();




        public static void TryWrite(this SerialPort This, string Content, ISerialPortArgs Args, int Timeout = 1000)
        {

            bool bSuccess = true;
            if (Locks.TryGetValue(This.PortName, out object Lock))
            {
                try
                {
                    Monitor.Enter(Lock);

                    // Check Args
                    bool IsArgsModified = false;
                    foreach (PropertyInfo Property in SerialPortArgs)
                        if (typeof(SerialPort).GetProperty(Property.Name) is PropertyInfo Info &&
                            Info.GetValue(This) is object Value &&
                            Property.GetValue(Args) is object ArgValue &&
                            !Value.Equals(ArgValue))
                        {

                            IsArgsModified = true;
                            if (This.IsOpen)
                                This.Close();

                            Info.SetValue(This, ArgValue);
                        }

                    if (IsArgsModified) 
                    {
                        This.Open();
                    }
                    else
                    {
                        This.DiscardInBuffer();
                        This.DiscardOutBuffer();
                    }

                    // Write
                    This.Write(Content); 

                }
                catch (Exception ex)
                {
           
                }
                finally
                {
                    Monitor.Exit(Lock);
                     
                }
            };
        }


        //public static void LockProtect()
        //{
        //    OpenDevice();
        //    sl.Enter(ref m_Lock);
        //}

        //public static void UnLock()
        //{
        //    sl.Exit(m_Lock);
        //}

        private static bool IsInitialized;
        private static void Init()
        {
            if (IsInitialized)
                return;

            foreach (string Name in SerialPort.GetPortNames())
                Register(Name);

            IsInitialized = true;

            //if (bDeviceInit)
            //{
            //    if (File.Exists(Path))
            //    {
            //        JToken Temp = JToken.Parse(File.ReadAllText(Path));
            //        foreach (JObject item in Temp)
            //            item.ToObject<SerialPort>().Register();
            //    }
            //    bDeviceInit = true;
            //}
        }

    }
}
