using Machine.Core.Enums;
using Machine.Core.Interfaces;
using System;
using System.Collections.Generic;

namespace Machine.Core
{
    public class cGM_Simulation_G : IGraber, IGraberBaseArgs, IGraberCamArgs, IGraber3DArgs, IComponent
    {
        public string Name { set; get; }

        public string UID { set; get; }

        public GrabModuleType Type => GrabModuleType.Simulation;

        public string CCD_Name { set; get; }

        public string CCD_ID { set; get; }

        public double Gain { set; get; }

        public double ExposureTime { set; get; } = 20;

        public double Rate { set; get; } = 35000d;

        public int FrameWidth { set; get; } = 16352;

        public int FrameHeight { set; get; } = 1000;

        public int FrameBufCount { set; get; } = 2;

        public double PixelWidth { set; get; } = 0.01;

        public double PixelHeight { set; get; } = 0.01;

        public string ConfigFileName { set; get; }
        public int PixelBytes { get; set; } = 3;
        public int BufHeight { get; set; } = 100000;
        public int BufWidth => FrameWidth;

        public string SsensorIP { set; get; } = "192.168.1.10";
        public bool UseAccelerator { set; get; } = false;
        public int GrabTimeout { set; get; } = 10000;

        public void GetBufAddress(out IntPtr[] Datas)
        {
            //throw new NotImplementedException();
            Datas = new IntPtr[]{ IntPtr.Zero };
        }

        public void GetBufAddress(out IntPtr R, out IntPtr G, out IntPtr B)
        {
            throw new NotImplementedException();
        }

        public void GetCurrentFrame(out  IntPtr[] Datas)
        {
            Datas = new IntPtr[3];
 
        }

        public void GetCurrentFrame(out IntPtr R, out IntPtr G, out IntPtr B)
        {
            throw new NotImplementedException();
        }

        public bool Init()
        {
            return true;
        }

        public void Start(bool IsInternal = true)
        {
        }

        public void Stop()
        {
        }
        public void SetFeatureValue(string featureName, object value)
        {
        }
        public object GetFeatureValue(string featureName)
        {
            try
            {
                switch (featureName)
                {
                    case "ExposureTime":
                        return ExposureTime;
                    case "PixelBytes":
                        return PixelBytes;
                    case "ScanRate":
                        return Rate;
                    case "BufferCount":
                        return FrameBufCount;
                    case "ScanLength":
                        return (double)BufHeight*PixelHeight;
                    default:
                        return default(object);
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }
        public void CallFunction(string functionName, List<object> args)
        {
            return;
        }
        public void Start()
        {
            Start(false);
        }

        public void Dispose()
        {
        }
    }
}
