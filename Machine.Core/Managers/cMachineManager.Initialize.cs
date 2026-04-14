using Machine.Core.Enums;
using Machine.Core.Interfaces;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

using MenthaAssembly;
using MenthaAssembly.Network;
using static Machine.Core.TCPComm;
using System.Diagnostics;
using System.Threading;

namespace Machine.Core
{
    public static partial class cMachineManager
    {
        public static string BaseDir { set; get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                       "MachineAssembly", Assembly.GetCallingAssembly().GetName().Name);
        const string AxisesFileName = @"Axises";
        const string LightsFileName = @"Lights";
        const string DIFileName = @"DInputs";
        const string DOFileName = @"DOutputs";
        const string PaltformFileName = @"Platforms";
        const string GrabModuleFileName = @"GrabModule";
        const string PlatformArgsFileName = @"PlatformArgs";

        /*
        const string AxisesPath = @"C:\Windows\System32\Setting_Vtest\Axises.txt";
        const string LightsPath = @"C:\Windows\System32\Setting_Vtest\Lights.txt";
        const string DIPath = @"C:\Windows\System32\Setting_Vtest\DInputs.txt";
        const string DOPath = @"C:\Windows\System32\Setting_Vtest\DOutputs.txt";
        const string PaltformPath = @"C:\Windows\System32\Setting_Vtest\Platforms.txt";
        const string GrabModulePath = @"C:\Windows\System32\Setting_Vtest\GrabModule.txt";
        const string PlatformArgsPath = @"C:\Windows\System32\Setting_Vtest\PlatformArgs.txt";
        */

        private static void LoadAxises()
        {

            foreach (var s in Directory.GetFiles(BaseDir).Where(i => i.Contains(AxisesFileName)))
            /*if (Directory.Exists(BaseDir) &&
                File.Exists(Path.Combine(BaseDir, AxisesFileName)))*/
            {
                JToken Temp = JToken.Parse(File.ReadAllText(Path.Combine(BaseDir, s)));



                var AxisesTypes = Assembly.GetAssembly(typeof(IAxis))
                                          .GetTypes()
                                          .Where(i => i.IsClass && !i.IsAbstract && i.GetInterface(nameof(IAxis)) != null);
                try
                {
                    foreach (JObject item in Temp)
                        if (AxisesTypes.FirstOrDefault(i => i.Name.Equals($"cAxis_{ (AxisCardType)item["Type"].Value<int>()}")) is Type AxisType)
                            Axises.Add(item["UID"].Value<string>(), (IAxis)item.ToObject(AxisType));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"{s} \r\n {ex}");
                }
            }
        }


        public static void SaveAxises() { }// => Axises.Values.ToJsonFile(Path.Combine(BaseDir, AxisesFileName));

        private static void LoadLights()
        {
            foreach (var s in Directory.GetFiles(BaseDir).Where(i => i.Contains(LightsFileName)))
            {
                JToken Temp = JValue.Parse(File.ReadAllText(Path.Combine(BaseDir, s)));


                var Types = Assembly.GetAssembly(typeof(ILight))
                                         .GetTypes()
                                         .Where(i => i.IsClass && !i.IsAbstract && i.GetInterface(nameof(ILight)) != null);
                try
                {
                    foreach (JObject item in Temp)


                        if (Types.FirstOrDefault(i => i.Name.Equals($"cLight_{ (LightType)item["Type"].Value<int>()}")) is Type Type)
                            Lights.Add(item["UID"].Value<string>(), (ILight)item.ToObject(Type));


                }
                catch (Exception ex)
                {
                    Console.WriteLine($"{s} \r\n {ex}");
                }
            }
        }

        public static void SaveLights() { }// => Lights.Values.ToJsonFile(Path.Combine(BaseDir, LightsFileName));


        private static void LoadDigitalInputs()
        {


            foreach (var s in Directory.GetFiles(BaseDir).Where(i => i.Contains(DIFileName)))
            {
                JToken Temp = JToken.Parse(File.ReadAllText(Path.Combine(BaseDir, s)));

                var Types = Assembly.GetAssembly(typeof(IDigitalInput))
                         .GetTypes()
                         .Where(i => i.IsClass && !i.IsAbstract && i.GetInterface(nameof(IDigitalInput)) != null);

                try
                {
                    foreach (JObject item in Temp)

                        if (Types.FirstOrDefault(i => i.Name.Equals($"cDI_{ (IOCardType)item["Type"].Value<int>()}")) is Type Type)
                            DInputs.Add(item["UID"].Value<string>(), (IDigitalInput)item.ToObject(Type));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"{s} \r\n {ex}");
                }
            }

        }
        public static void SaveDigitalInput() { }//=> DInputs.Values.ToJsonFile(Path.Combine(BaseDir, DIFileName));


        private static void LoadDigitalOutputs()
        {

            foreach (var s in Directory.GetFiles(BaseDir).Where(i => i.Contains(DOFileName)))
            {
                JToken Temp = JValue.Parse(File.ReadAllText(Path.Combine(BaseDir, s)));



                var Types = Assembly.GetAssembly(typeof(IDigitalOutput))
         .GetTypes()
         .Where(i => i.IsClass && !i.IsAbstract && i.GetInterface(nameof(IDigitalOutput)) != null);

                try
                {
                    foreach (JObject item in Temp)
                        if (Types.FirstOrDefault(i => i.Name.Equals($"cDO_{ (IOCardType)item["Type"].Value<int>()}")) is Type Type)
                            DOutputs.Add(item["UID"].Value<string>(), (IDigitalOutput)item.ToObject(Type));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"{s} \r\n {ex}");
                }
            }

        }
        public static void SaveDigitalOutputs() { }// => DOutputs.Values.ToJsonFile(Path.Combine(BaseDir, DOFileName));

        private static void LoadGrabModels()
        {
            foreach (var s in Directory.GetFiles(BaseDir).Where(i => i.Contains(GrabModuleFileName)))
            {
                JToken Temp = JValue.Parse(File.ReadAllText(Path.Combine(BaseDir, Path.GetFileName (s))));


                var Types = Assembly.GetAssembly(typeof(ICamera))
                    .GetTypes()
                    .Where(i => i.IsClass && !i.IsAbstract && i.GetInterface(nameof(ICamera)) != null);
                try
                {
                    foreach (JObject item in Temp)
                        if (Types.FirstOrDefault(i => i.Name.Equals($"cGM_{ (GrabModuleType)item["Type"].Value<int>()}")) is Type Type)
                            Cameras.Add(item["UID"].Value<string>(), (ICamera)item.ToObject(Type));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"{s} \r\n {ex}");
                }
            }

        }
        public static void SaveGrabModel() => Cameras.Values.ToJsonFile(Path.Combine(BaseDir, GrabModuleFileName));

        private static void LoadPlatformArgs()
        {


            foreach (var s in Directory.GetFiles(BaseDir).Where(i => i.Contains(PlatformArgsFileName)))
            {
                JToken Temp = JValue.Parse(File.ReadAllText(Path.Combine(BaseDir, s)));

                try
                {
                    foreach (JObject item in Temp)
                    {
                        PlatformArgs.Add(item["UID"].Value<string>(), item.ToObject<cPlatform_General>());
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"{s} \r\n {ex}");
                }
            }



        }
        public static void SavePlatformArg() { }//=> PlatformArgs.Values.ToJsonFile(Path.Combine(BaseDir, PlatformArgsFileName));



        public static void LoadComponents()
        {
            LoadAxises();
            LoadLights();
            LoadDigitalInputs();
            LoadDigitalOutputs();
            LoadGrabModels();
            LoadPlatformArgs();

        }



        static void NetCompoentsFindIP()
        {

           // foreach (var o in Axises.Values)
           //     if (o is cAxis_Net O)
           //     {
           //         do
           //         {
           //             O.IP = "";
           //             string a = o.UID;
           //             foreach (var ip in TCPComm.Client_IP_PORT.Keys)
           //             {
           //                 O.IP = ip;
           //                 if (!(bool)TCPComm.Send(O, null, "GetEntityComponent_IP"))
           //                     O.IP = "";
           //                 else
           //                     break;
           //             }
           //             Thread.Sleep(100);

           //         }
           //         while (O.IP == "");
           //     }

           // foreach (var o in Lights.Values)
           //     if (o is cLight_Net O)
           //     {
           //         do
           //         {
           //             O.IP = "";
           //             foreach (var ip in TCPComm.Client_IP_PORT.Keys)
           //             {
           //                 O.IP = ip;
           //                 if (!(bool)TCPComm.Send(O, null, "GetEntityComponent_IP"))
           //                     O.IP = "";
           //                 else
           //                     break;
           //             }
           //             Thread.Sleep(100);
           //         }
           //         while (O.IP == "");
           //     }

           // foreach (var o in DInputs.Values)
           // {
           //     if (o is cDI_Net O)
           //     {
           //         do
           //         {
           //             O.IP = "";
           //             foreach (var ip in TCPComm.Client_IP_PORT.Keys)
           //             {
           //                 O.IP = ip;
           //                 if (!(bool)TCPComm.Send(O, null, "GetEntityComponent_IP"))
           //                     O.IP = "";
           //                 else
           //                     break;
           //             }
           //             Thread.Sleep(100);
           //         }
           //         while (O.IP == "");
           //     }
           // }
           // foreach (var o in DOutputs.Values)
           // {
           //     if (o is cDO_Net O)
           //     {
           //         do
           //         {
           //             O.IP = "";
           //             foreach (var ip in TCPComm.Client_IP_PORT.Keys)
           //             {
           //                 O.IP = ip;
           //                 if (!(bool)TCPComm.Send(O, null, "GetEntityComponent_IP"))
           //                     O.IP = "";
           //                 else
           //                     break;
           //             }
           //             Thread.Sleep(100);
           //         }
           //         while (O.IP == "");
           //     }
           // }
           // Console.WriteLine("ClitenSendReady");
           // //Client_Init  = true;
           //// ClitenSendReady();


           // Console.WriteLine("Client_Init");

            //{

            //        foreach (var ip in TCPCommunication.Client_IP_PORT.Keys)
            //        {
            //            if (ip == TCPCommunication.IP)
            //                continue;
            //            Task.Run(() =>
            //           {

            //               {
            //                   foreach (var o in Axises.Values)
            //                       if (o is cAxis_Net O)
            //                       {
            //                           O.IP = ip;
            //                           object a = TCPCommunication.ClitenSend(O, null, "GetEntityComponent_IP");
            //                           if (!(bool)a)

            //                               O.IP = "";
            //                       }

            //                   foreach (var o in Lights.Values)
            //                       if (o is cLight_Net O)
            //                       {
            //                           O.IP = ip;
            //                           if (!(bool)TCPCommunication.ClitenSend(O, null, "GetEntityComponent_IP"))
            //                               O.IP = "";
            //                       }
            //               /*
            //               foreach (var o in Cameras.Values)
            //               {
            //                   if (o is cGM_Net O)
            //                   {
            //                       O.IP = ip;
            //                       if (!(bool)TCPCommunication.ClitenSend(O, null, "GetEntityComponent_IP"))
            //                           O.IP = "";
            //                   }
            //               }*/

            //                   foreach (var o in DInputs.Values)
            //                   {
            //                       if (o is cDI_Net O)
            //                       {
            //                           O.IP = ip;
            //                           if (!(bool)TCPCommunication.ClitenSend(O, null, "GetEntityComponent_IP"))
            //                               O.IP = "";
            //                       }
            //                   }

            //                   foreach (var o in DOutputs.Values)
            //                   {
            //                       if (o is cDO_Net O)
            //                       {
            //                           O.IP = ip;
            //                           if (!(bool)TCPCommunication.ClitenSend(O, null, "GetEntityComponent_IP"))
            //                               O.IP = "";
            //                       }
            //                   }
            //               /*
            //               foreach (var o in PlatformArgs.Values)
            //               {
            //                   if (o is cPlatformArgs_Net O)
            //                   {
            //                       O.IP = ip;
            //                       if (!TCPCommunication.ClitenSend<bool>(O, null, "GetEntityComponent_IP"))
            //                           O.IP = "";
            //                   }
            //               }*/
            //               }


            //           });

            //        }

            //    }
            //}

        }
    }
}
