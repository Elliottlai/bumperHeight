using Machine.Core.Interfaces;
using Machine.Core;
using System.Collections.Generic;
using System.Threading.Tasks;
using System;
using System.Threading;
using System.Linq;
using System.Reflection;
using System.IO;
using Newtonsoft.Json;


namespace Machine.Core
{

    public static partial class cMachineManager
    {
        public static Dictionary<string, IAxis> Axises { set; get; } = new Dictionary<string, IAxis>();

        public static Dictionary<string, ILight> Lights { set; get; } = new Dictionary<string, ILight>();

        public static Dictionary<string, ICamera> Cameras { set; get; } = new Dictionary<string, ICamera>();

        public static Dictionary<string, IDigitalInput> DInputs { set; get; } = new Dictionary<string, IDigitalInput>();

        public static Dictionary<string, IDigitalOutput> DOutputs { set; get; } = new Dictionary<string, IDigitalOutput>();

        public static Dictionary<string, IPlatformArgs> PlatformArgs { set; get; } = new Dictionary<string, IPlatformArgs>();

        //public static List<IAxis> Axises { set; get; } = new List<IAxis>();

        //public static List<ILight> Lights { set; get; } = new List<ILight>();

        //public static List<ICamera> Cameras { set; get; } = new List<ICamera>();

        //public static List<IDigitalInput> DInputs { set; get; } = new List<IDigitalInput>();

        //public static List<IDigitalOutput> DOutputs { set; get; } = new List<IDigitalOutput>();

        //public static List<IPlatformArgs> PlatformArgs { set; get; } = new List<IPlatformArgs>();

        //   public static List<Part> Parts { get; } = new List<Part>();
        /*public class cCommConfig
        {
            public string MachineName { get; set; } = "MachineName";
            public string registerIP { get; set; } = "127.0.0.1";
            public int registerPort { get; set; } = 168;
            public bool register { get; set; } = true;
            public void Save(string FileName) => this.ToJsonFile(FileName);
        }*/
        public static cCommConfig config = new cCommConfig();



        private static bool IsInitialize = false;
        public static void Init(string _BaseDir = null)
        {

            if (IsInitialize)
                return;
 

            BaseDir = string.IsNullOrEmpty(_BaseDir) ?
                      Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                                   "MachineAssembly",
                                   Assembly.GetCallingAssembly().GetName().Name) :
                      _BaseDir;



            if (!File.Exists(BaseDir + ".cfg"))
                config.Save(BaseDir + ".cfg");


            try
            {
                config = JsonHelper.Load<cCommConfig>(BaseDir + ".cfg");

            }
            catch (Exception ex)
            {

            }

            if (!Directory.Exists(BaseDir))
                Directory.CreateDirectory(BaseDir);

            LoadComponents();
            TCPComm.CommStart(config);
            //NetCompoentsFindIP();

            IsInitialize = true;
        }


        public static void SwitchCameraTrigger(IAxis axis, bool status)
        {
            IEnumerable<IDigitalOutput> DO =
            from Douts in DOutputs.Values
            where Douts.Name.Contains(axis.UID.ToString()) && Douts.Name.Contains("Select")
            select Douts;

            if (DO.Count() == 1)
                DO.FirstOrDefault().SetStatus(status);
            else if (DO.Count() > 1)
                throw new Exception(" Axis more than one AixsSelect");

        }

        public static void Scan(ICamera Camera,
            MotionInfo MotionX, MotionInfo MotionY, MotionInfo MotionZ,
            out IntPtr[] Datas)
        {
            double StartY = MotionY.Axis.GetRealPosition();
            SwitchCameraTrigger(MotionY.Axis, true);

            Camera.Init();

            Datas = Camera.GetBufAddress();
            MotionX.Axis.SetSVON(true);
            MotionY.Axis.SetSVON(true);
            MotionZ.Axis.SetSVON(true);
            MotionX.Axis.SetMaxVel(MotionX.Axis.OperationSpeed);
            MotionY.Axis.SetMaxVel(MotionY.Axis.OperationSpeed);
            MotionZ.Axis.SetMaxVel(MotionZ.Axis.OperationSpeed);
            Thread.Sleep(1000);
            MotionX.Axis.MotMoveAbs(MotionX.End);
            MotionY.Axis.MotMoveAbs(MotionY.Start);
            MotionZ.Axis.MotMoveAbs(MotionZ.End);

            do
            {
                Thread.Sleep(100);
            }
            while (!(MotionX.Axis.Wait() && MotionY.Axis.Wait() && MotionZ.Axis.Wait()));

            Camera.Start();
            MotionY.Axis.SetMaxVel(Camera.Rate * Camera.PixelHeight);
            MotionY.Axis.MotMoveAbs(MotionY.End + 10);

            do
            {
                Thread.Sleep(100);
            }
            while (!MotionY.Axis.Wait());

            Camera.Stop();
            SwitchCameraTrigger(MotionY.Axis, false);
            MotionY.Axis.SetMaxVel(MotionY.Axis.OperationSpeed);
            MotionY.Axis.MotMoveAbs(StartY);

            do
            {
                Thread.Sleep(100);
            }
            while (!MotionY.Axis.Wait());


        }

        public static void Scan(ICamera Camera,
            MotionInfo MotionX, MotionInfo MotionY, MotionInfo MotionZ,
            out IntPtr R, out IntPtr G, out IntPtr B)
        {
            double StartY = MotionY.Axis.GetRealPosition();
            SwitchCameraTrigger(MotionY.Axis, true);
            Camera.Init();

            IntPtr[] data = Camera.GetBufAddress();
            R = data[0];
            G = data[1];
            B = data[2];


            MotionX.Axis.SetSVON(true);
            MotionY.Axis.SetSVON(true);
            MotionZ.Axis.SetSVON(true);

            MotionX.Axis.MotMoveAbs(MotionX.End);
            MotionY.Axis.SetMaxVel(Camera.Rate * Camera.PixelHeight);
            MotionY.Axis.MotMoveAbs(MotionY.Start);
            MotionZ.Axis.MotMoveAbs(MotionZ.End);

            Thread.SpinWait(50);

            while (!(MotionX.Axis.Wait() && MotionY.Axis.Wait() && MotionZ.Axis.Wait()))
                Thread.SpinWait(10);

            Camera.Start();

            MotionY.Axis.MotMoveAbs(MotionY.End + 10);

            //MotionY.Axis.MotMoveAbs(MotionY.End + 1000);

            while (!MotionY.Axis.Wait())
                Thread.SpinWait(10);

            Camera.Stop();
            SwitchCameraTrigger(MotionY.Axis, false);

            MotionY.Axis.MotMoveAbs(StartY);

            //MotionY.Axis.MotMoveAbs(MotionY.End + 1000);

            while (!MotionY.Axis.Wait())
                Thread.SpinWait(100);
        }

        //static HImage[] GrabImg = null;
        //static int BufID = 0;
        //static bool Saving = false;
        //public static void Save()
        //{
        //    if (Saving == true)
        //    {
        //        Saving = false;
        //        return;
        //    }
        //    cGM_Dalsa_0 Camera = (cGM_Dalsa_0)Cameras["Line1"];
        //    if (GrabImg == null)
        //    {
        //        GrabImg = new HImage[cGM_Dalsa_0.TotalGrabCount];

        //        long size = Camera.FrameWidth;
        //        size = size * (Camera.BufHeight);
        //        unsafe
        //        {
        //            for (int i = 0; i < GrabImg.Length; i++)
        //            {

        //                IntPtr[] rgb = Camera.GetBufAddress(i);

        //                //   IntPtr DataS = Dasal_1.GetBufAddress(i);
        //                byte* DataS = (byte*)rgb[0];
        //                byte* DataS1 = DataS + size;
        //                byte* DataS2 = DataS1 + size;
        //                string type = "byte";

        //                GrabImg[i] = new HImage();
        //                //if (Camera.m_PiranhaXL.m_Buffer.BytesPerPixel == 3)
        //                GrabImg[i].GenImage3Extern(type, Camera.FrameWidth, Camera.BufHeight  , rgb[0], rgb[1], rgb[2], IntPtr.Zero);

        //                GrabImg[i].GetImageSize(out int w, out int h);

        //            }
        //        }
        //    }
        //    Saving = true;
        //    while (Saving)
        //    {
        //        if (BufID != Camera.GetCurrentBufID())
        //        {
        //            try
        //            {
        //                int bb = Camera.GetCurrentBufID();
        //                HObject a = new HObject();
        //                 HOperatorSet.ZoomImageFactor(GrabImg[bb], out a, 0.25, 0.25, "constant");
        //                String filename = System.DateTime.Now.ToString("HH_mm_ss");
        //                HOperatorSet.WriteImage(a, "tiff", 0, $"d:\\temp_{bb}_{filename}_.tiff");
        //                BufID = Camera.GetCurrentBufID();
        //            }
        //            catch (Exception ex)
        //            {
        //                ;
        //            }
        //        }
        //        Thread.Sleep(100);
        //    }



        //}



    }


}
