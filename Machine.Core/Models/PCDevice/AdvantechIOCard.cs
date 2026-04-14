using Automation.BDaq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Machine.Core
{
    public class AdvantechIOCard
    {


        //static DeviceInformation deviceInfo;
        static bool isInitialized;
        static int deviceId = 0;

        public static InstantDoCtrl DO;
        public static InstantDiCtrl DI;

        public static bool OpenDevice()
        {
            bool bSuccess = false;
            if (!isInitialized)
            {
                /*
                DeviceInformation t;
                t = new DeviceInformation(5);*/

                DO = new InstantDoCtrl();
                DI = new InstantDiCtrl();
                for (int i = 0; i < DO.SupportedDevices.Count; i++)
                {


                    if (DO.SupportedDevices[i].Description.Contains("1756"))  // .ElementAt(i).ToString() != "No device")
                    {

                        try
                        {

                            int id = DO.SupportedDevices[i].DeviceNumber;
                            DI.SelectedDevice = new DeviceInformation(id);
                            DO.SelectedDevice = new DeviceInformation(id);
                            bSuccess = true;
                            isInitialized = true;
                            break;
                        }
                        catch { continue; }

                    }
                }



            }
            return bSuccess;
        }

        private static object Lock = new object();

        public static void LockProtect()
        {


            Monitor.Enter(Lock);
            OpenDevice();

        }

        public static void UnLock()
        {
            Monitor.Exit(Lock);
        }

        public static T Protect<T>(Func<T> Function)
        {
            try
            {
                /*while (Monitor.IsEntered(Lock))
                    Thread.Sleep(100);*/

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
}
