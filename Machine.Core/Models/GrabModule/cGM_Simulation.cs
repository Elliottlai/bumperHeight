using Machine.Core.Enums;
using Machine.Core.Interfaces;
using System;

namespace Machine.Core
{
    public class cGM_Simulation : ICamera
    {
        public GrabModuleType Type => GrabModuleType.Simulation;

        public string CCD_Name { get; set; }
        public string CCD_ID { get; set; }
        public double Gain { get; set; }
        public double ExposureTime { get; set; }
        public double Rate { get; set; }

        public int PixelBytes { get; set; }

        public int FrameWidth { get; set; }
        public int FrameHeight { get; set; }
        public double PixelWidth { get; set; }
        public double PixelHeight { get; set; }
        public string ConfigFileName { get; set; }
        public string UID { get; set; }
        public string Name { get; set; }
        public int BufWidth { get; set; }
        public int BufHeight { get; set; }

        public object FunctionCall(GMExpandFunction func, params object[] value)
        {
            return null;
        }

        public IntPtr[] GetBufAddress(int index = -1)
        {
            return null;
        }

        public IntPtr[] GetCurrentFrame()
        {
            return null;
        }

        public object GetFeatureValue(GMExpandParamter Param)
        {
            return 0;
        }



        public bool Init()
        {
            return true;
        }

        public bool SetFeatureValue(GMExpandParamter Param, params object[] value)
        {
            return true ;
        }

        public void Start(int BufIndex = -1)
        {
            ;
        }

        public void Stop()
        {
            ;
        }
    }
}
