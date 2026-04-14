using Machine.Core.Enums;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
 


namespace Machine.Core
{
    class ModbusTcpIOCard
    {
        static bool isInitialized;
        static string CfgPath = @"C:\ProgramData\MachineAssembly\";
        public static List<ModbusDevice> modbus = new List<ModbusDevice>();


        public static bool OpenDevice()
        {
            
            bool bSuccess = false;

            if (!isInitialized)
            {
                // 根據設定檔新增Modbus
                const string ModbusFileName = @"ModbusSettings.txt";
            
                string filepath;
                string BaseDir = string.IsNullOrEmpty(CfgPath) ?
                             Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                             "MachineAssembly",
                             Assembly.GetCallingAssembly().GetName().Name) :
                             CfgPath;
                ModbusSet ModbusSets = new ModbusSet();
                //var NowVer = FileVersionInfo.GetVersionInfo($@"{Directory.GetCurrentDirectory()}\SynPower.Avis.MachZDT.dll");

                filepath = Path.Combine(BaseDir, ModbusFileName);
                if (!File.Exists(filepath))
                    ModbusSets.SaveDefault(filepath);
                ModbusSets = JsonHelper.Load<ModbusSet>(filepath);
                //ModbusSets.TryUpdate(NowVer, filepath);



                /////
                try
                {
                    for (int i = 0; i != ModbusSets.Quantity; i++)
                    {
                        ModbusDevice temp = new ModbusDevice();
                        bool check = temp.InitialDevice(ModbusSets[i].OUT, ModbusSets[i].IN, ModbusSets[i].IP, ModbusSets[i].PORT);
                        
                        if (!check)
                        {
                            
                            isInitialized = false;
                            bSuccess = false;
                            throw new InvalidOperationException("Modbus Connect Failed");
                            break;
                        }
                        else
                        {
                            modbus.Add(temp);
                            isInitialized = true;
                            bSuccess = true;
                        }
                        
                    }

                }
                catch { }
            }
            return bSuccess;
        }


        public static void LockProtect()
        {
            while (!Monitor.TryEnter(Lock))
                Thread.Sleep(100);



            OpenDevice();

        }

        public static void UnLock()
        {
            Monitor.Exit(Lock);
        }

        private static object Lock = new object();
        public static T Protect<T>(Func<T> Function)
        {
            try
            {
                //while (Monitor.IsEntered(Lock))
                //    Thread.Sleep(100);

                Monitor.Enter(Lock);

                OpenDevice();
                if (isInitialized)
                    return Function();
                else
                    return default(T);
            }
            finally
            {
                Monitor.Exit(Lock);
            }
        }
        public static void Protect(Action Action)
        {
            try
            {
                /* while (Monitor.IsEntered(Lock))
                     Thread.Sleep(100);*/

                Monitor.Enter(Lock);

                OpenDevice();
                if (isInitialized)
                    Action();
            }
            finally
            {
                Monitor.Exit(Lock);
            }
        }

    }



    public struct ModbusIOSetting
    {
        //IOArgs----------
        public string IP;
        public ushort OUT;
        public ushort IN;
        public ushort PORT;
        public IOCardType Type;
        public ModbusIOSetting(string uid)    //struct不可定義"無參數"結構方法(預設且不可變更)
        {
            IP = uid;
            OUT = 0;
            IN = 0;
            PORT = 0;
            Type = IOCardType.Simulation;
        }
    }
    [JsonObject(MemberSerialization.Fields)]
    public class ModbusSet : IEnumerable<ModbusIOSetting>
    {
        /// <summary>設定檔版本</summary>
        public string SettingVer { get; private set; }
        [JsonIgnore]
        /// <summary>設定檔路徑</summary>
        private string ConfigPath;
        [JsonIgnore]
        /// <summary>通道數量</summary>
        public int Quantity { get { return settings.Count; } }
        private List<ModbusIOSetting> settings;

        public ModbusSet()
        {
            this.settings = new List<ModbusIOSetting>();
        }

        public void Save(string file)
        {
            this.ToJsonFile(file);
        }

        public void SaveDefault(string file)
        {
            ConfigPath = file;

            settings.Clear();

            for (ushort c = 0; c < 4; c++)
            {
                for (ushort b = 0; b < 8; b++)
                {
                    settings.Add(new ModbusIOSetting($"DIO_{c * 8 + b}")
                    {
                        IP = $"DIO_{c * 8 + b}",
                        OUT = c,
                        IN = c,
                        PORT = b,
                        Type = IOCardType.Simulation,
                    });
                }
            }
            Save(file);
        }

        public bool TryUpdate(FileVersionInfo newVer, string file)
        {
            try
            {
                if (SettingVer == null || SettingVer == string.Empty)
                    SettingVer = "2.50.0.0";
                var verPatrs = SettingVer.Split('.');
                if (Convert.ToInt32(verPatrs[1]) == newVer.FileMinorPart)
                {
                    if (Convert.ToInt32(verPatrs[2]) < newVer.FileBuildPart)
                    {
                        SettingVer = newVer.FileVersion;
                        Save(file);
                        return true;
                    }
                    else
                        return true;
                }
                else
                    return false;
            }
            catch
            {
                return false;
            }
        }

        public ModbusIOSetting this[string IP]
        {
            get
            {
                if (this.settings.Any(ioset => ioset.IP == IP))
                    return this.settings.First(ioset => ioset.IP == IP);
                else
                    throw new InvalidOperationException($"IOSettings 沒有與 {IP} 相符的元素");
            }
        }

        public ModbusIOSetting this[int id]
        {
            get
            {
                return this.settings[id];
            }
        }

        public void Add(ModbusIOSetting setting)
        {
            settings.Add(setting);
        }

        public void Clear()
        {
            settings.Clear();
        }

        public IEnumerator<ModbusIOSetting> GetEnumerator()
        {
            return this.settings.GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return this.settings.GetEnumerator();
        }
    }

}
