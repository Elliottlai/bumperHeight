using Advantech.Motion;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Machine.Core
{
    public class AdvantechAxisCard
    {
        private static bool bDeviceInit = false;

        public static uint BoardCount { get; private set; }
        public static uint AxisCount { get; private set; }
        public static string[] configFiles { get; private set; }
        public static IntPtr[] axisHandles { get; private set; }
        public static IntPtr[] devHandles { get; private set; }
        public static string[] CardName { get; private set; }

        public static string[] SupportNames { get; } = { "1245", "1265", "1285" };

        public static bool OpenDevice()
        {
            bool bSuccess = false;
            if (!bDeviceInit)
            {

                bSuccess = true;
                uint devCnt = 0;
                DEV_LIST[] currentAvailableDevs = new DEV_LIST[Motion.MAX_DEVICES];
                axisHandles = new IntPtr[Motion.MAX_DEVICES];
                CardName = new string[Motion.MAX_DEVICES];

                uint Err = (uint)Motion.mAcm_GetAvailableDevs(currentAvailableDevs, Motion.MAX_DEVICES, ref devCnt);
                if (Err != (uint)ErrorCode.SUCCESS)
                    bSuccess = false;

                if (devCnt > 0)
                {
                    configFiles = new string[devCnt];
                    devHandles = new IntPtr[devCnt];
                    System.Threading.Thread.Sleep(50);



                    BoardCount = devCnt;
                    AxisCount = 0;


                    for (int i = 0; i < devCnt; i++)
                    {
                        if (currentAvailableDevs[i].DeviceName.Contains("1245") || currentAvailableDevs[i].DeviceName.Contains("1285") || currentAvailableDevs[i].DeviceName.Contains("1265"))
                        {

                            uint err = (uint)Motion.mAcm_DevOpen(currentAvailableDevs[i].DeviceNum, ref devHandles[i]);
                            System.Threading.Thread.Sleep(50);

                            if (err == (uint)ErrorCode.SUCCESS)
                            {


                                uint axisCntPerDev = 0;
                                uint buffLen = (uint)Marshal.SizeOf(axisCntPerDev);


                                //FT_DevAxisCount = 1;
                                err = Motion.mAcm_GetProperty(devHandles[i], (uint)PropertyID.FT_DevAxesCount, ref axisCntPerDev, ref buffLen);



                                for (int j = 0; j < axisCntPerDev; j++)
                                {
                                    CardName[AxisCount] = currentAvailableDevs[i].DeviceName;
                                    uint err1 = (uint)Motion.mAcm_AxOpen(devHandles[i], (ushort)j, ref axisHandles[AxisCount]);
                                    if (err1 != 0)
                                        throw new InvalidOperationException("AdvantechAxisCard open Axis error");
                                    double Actual = 0;

                                    err1 = (uint)Motion.mAcm_AxStopDec(AdvantechAxisCard.axisHandles[j]);
                                    Thread.Sleep(20);
                                    err1 = (uint)Motion.mAcm_AxGetActualPosition(AdvantechAxisCard.axisHandles[j], ref Actual);
                                    Thread.Sleep(20);
                                    err1 = (uint)Motion.mAcm_AxSetCmdPosition(AdvantechAxisCard.axisHandles[j], Actual);
                                    Thread.Sleep(20);

                                    AxisCount++;


                                }
                                System.Threading.Thread.Sleep(50);
                                ///取得研華卡片的指撥開關編號

                                uint cardno = (currentAvailableDevs[i].DeviceNum  >> 12 )& 0x00000fff;
                                //暫時TEST
                                err = Motion.mAcm_DevLoadConfig(devHandles[i], $@"C:\ProgramData\MachineAssembly\motion{cardno}.cfg");
                                for (int j = 0; j < axisCntPerDev; j++)
                                {
                                    int disable = 0;

                                    Motion.mAcm_SetProperty(axisHandles[j],
                                                                                  (uint)global::Advantech.Motion.PropertyID.CFG_AxCamDOEnable,
                                                                                  ref disable,
                                                                                  (uint)Marshal.SizeOf(typeof(int)));

                                }




                                //err = Motion.mAcm_DevLoadConfig(PCI_1245.devHandles[PCI_1245.BoardTotalCount], @"D:\27000000.cfg");
                                if (Err != (uint)ErrorCode.SUCCESS)
                                    bSuccess = false;
                            }
                            else
                            {
                                bSuccess = false;
                                //throw new InvalidOperationException("AdvantechAxisCard open Device error");
                            }
                        }
                    }
                }
                else
                {
                    throw new InvalidOperationException("No AdvantechAxisCard Card");
                    // bSuccess = false;
                }
                bDeviceInit = bSuccess;


            }

            return bSuccess;
        }
        public static bool CloseDevice()
        {

            for (int i = 0; i < AxisCount; i++)
            {

                uint err = (uint)Motion.mAcm_AxClose(ref axisHandles[i]);
                if (err != 0)
                    throw new InvalidOperationException("AdvantechAxisCard Close Axis error");
            }
            for (int i = 0; i < BoardCount; i++)
            {
                uint err = (uint)Motion.mAcm_DevClose(ref devHandles[i]);
                if (err != 0)
                    throw new InvalidOperationException("AdvantechAxisCard Close Device error");
            }

            return true;
        }


        //private static bool m_Lock;
        //private static SpinLock sl = new SpinLock();
        private static object Lock = new object();

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


        public static T Protect<T>(Func<T> Function, bool Protect = true)
        {
            try
            {
                /*while (Monitor.IsEntered(Lock))
                    Thread.Sleep(100);*/
                if (Protect)
                    Monitor.Enter(Lock);

                OpenDevice();
                if (bDeviceInit)
                    return Function();
                else
                    return default(T);
            }
            finally
            {
                if (Protect)
                    Monitor.Exit(Lock);
            }
        }
        public static void Protect(Action Action)
        {
            try
            {


                Monitor.Enter(Lock);

                OpenDevice();
                if (bDeviceInit)
                    Action();
            }
            finally
            {
                Monitor.Exit(Lock);
            }
        }

        /*
        return AdvantechAxisCard.Protect(()=> 
        {
            double command = 0;
            uint err = Motion.mAcm_AxGetCmdPosition(AdvantechAxisCard.axisHandles[AxisID], ref command);

            if (err != (uint)ErrorCode.SUCCESS)
            {
                // throw new InvalidOperationException("Advantech Axis Card GetCmdPosition error");
            }
            return command * Scale;
        });
        */

    }
}
