using Machine.Core.Enums;
using Basler.Pylon;
using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Threading;

namespace Machine.Core
{
    public class cGM_Basler_Area : Interfaces.ICamera, IDisposable
    {
        public GrabModuleType Type => GrabModuleType.Basler_Area;

        public string CCD_Name { get; set; }
        public string CCD_ID { get; set; }
        public double Gain { get; set; }
        public double ExposureTime { get; set; }
        public double Rate { get; set; }

        public int PixelBytes
        {
            get => 3;// (int)BaseCamera.Parameters[PLCamera. ].GetValue();
            set { }
        }
        int frameWidth;
        public int FrameWidth
        {
            get { Init(); return frameWidth; }
            set { }
        }
        int frameHeight;
        public int FrameHeight
        {
            get { Init(); return frameHeight; }
            set { }
        }
        public double PixelWidth { get; set; }
        public double PixelHeight { get; set; }
        public string ConfigFileName { get; set; }
        public string UID { get; set; }
        public string Name { get; set; }

        private byte[] Datas;
        private Camera BaseCamera { set; get; }
       
        private PixelDataConverter Converter { set; get; }
        public int BufWidth { get { return FrameWidth; } set{ }  }
        public int BufHeight { get { return FrameHeight; } set{ }  }





        ~cGM_Basler_Area() {

            if (BaseCamera != null)
                BaseCamera.Close();
        }
        bool bInit = false;
        public bool Init()
        {


            if (bInit)
                return true;
             
             


            try
            {
                if (BaseCamera is null)
                {
                    List<ICameraInfo> allCameras = CameraFinder.Enumerate();
                    if (CameraFinder.Enumerate().FirstOrDefault() is ICameraInfo Info)
                    {
                        BaseCamera = new Camera(Info);
                    }
                    else
                    {
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                ;
            }
            if (BaseCamera.IsOpen)
                return true;

            try {
                BaseCamera.Open();

            } catch {
               // NetworkAdapter..RebootNetAdapter( );
                var adapters = NetworkAdapter.FindAll();
                foreach (var adp in adapters)
                {

                    if(adp.Name == "Basler GigE Vision Adapter")
                        try
                        {
                            adp.Disable();
                            adp.Enable();

                           
                            Thread.Sleep(10000);
                        }
                        catch
                        {
                            continue;
                        }
                }


            }
             


            try
            {

                if (!BaseCamera.IsOpen)
                    BaseCamera.Open();

                frameWidth = (int)BaseCamera.Parameters[PLCamera.Width].GetValue();
                frameHeight = (int)BaseCamera.Parameters[PLCamera.Height].GetValue();
                BaseCamera.StreamGrabber.ImageGrabbed += OnImageGrabbed;
                Converter = new PixelDataConverter { OutputPixelFormat = PixelType.RGB8planar };
                InitBuf(frameWidth, frameHeight);
                bInit = true;
                //Grab().Wait();
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);

            }
            return false;
        }
        Stopwatch Stopwatch = new Stopwatch ();
        private void OnImageGrabbed(object sender, ImageGrabbedEventArgs e)
        {

            InitBuf(e.GrabResult.Width, e.GrabResult.Height);
            

            try
            {
                if (e.GrabResult.IsValid &&
                    (!Stopwatch.IsRunning || Stopwatch.ElapsedMilliseconds > 5))
                {

                    Stopwatch.Restart();
                    Converter.Convert(Datas, e.GrabResult);
                }
                Stopwatch.Reset();

            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
            finally
            {
                e.DisposeGrabResultIfClone();
            }
        }

        public void Start(bool IsInternal = true)
        {
            try
            {
                if (Init())
                {
                    IsInternal = false;

                    if (IsInternal)
                    {
                        BaseCamera.Parameters[PLCamera.AcquisitionMode].SetValue(PLCamera.AcquisitionMode.Continuous);
                        BaseCamera.StreamGrabber.Start(GrabStrategy.OneByOne, GrabLoop.ProvidedByStreamGrabber);
                        BaseCamera.Parameters[PLCamera.TriggerMode].SetValue(PLCamera.TriggerMode.Off);
                    }
                    else {
                        Stop();                        
                        BaseCamera.Parameters[PLCamera.AcquisitionMode].SetValue(PLCamera.AcquisitionMode.Continuous);
                        BaseCamera.Parameters[PLCamera.TriggerMode].SetValue(PLCamera.TriggerMode.Off);
                        //BaseCamera.StreamGrabber.Start(GrabStrategy.OneByOne, GrabLoop.ProvidedByStreamGrabber);

                        //BaseCamera.Parameters[PLCamera.TriggerSelector].SetValue(PLCamera.TriggerSelector.FrameStart);
                        //BaseCamera.Parameters[PLCamera.TriggerMode].SetValue(PLCamera.TriggerMode.On);
                        //BaseCamera.Parameters[PLCamera.TriggerSource].SetValue(PLCamera.TriggerSource.Line1);

                        BaseCamera.Parameters[PLCamera.GainSelector].SetValue(PLCamera.GainSelector.All);

                        BaseCamera.Parameters[PLCamera.GainRaw].SetValue(10);
                        BaseCamera.Parameters[PLCamera.ExposureTimeAbs].SetValue(60000);

                        bool Reset = false;
                        double Sec = 0;
                        //BaseCamera.Parameters[PLCamera.GlobalResetReleaseModeEnable].SetValue(Reset);
                      //  BaseCamera.Parameters[PLCamera.TriggerDelayAbs].SetValue(Sec);
                        //frameHeight = 500;
                        //BaseCamera.Parameters[PLCamera.Height].SetValue(frameHeight);
                       /* Datas = null;
                        InitBuf(frameWidth, frameHeight);*/

                        //  BaseCamera.StreamGrabber.Start ()
                        //BaseCamera.Parameters[PLCamera.AcquisitionStart].Execute();
                        //BaseCamera.StreamGrabber.Start(100);



                        if (ImageGrabbedAdded)
                        {
                            ImageGrabbedAdded = false;
                            BaseCamera.StreamGrabber.ImageGrabbed-= ImageGrabbed;
                        }
                        BaseCamera.StreamGrabber.ImageGrabbed += ImageGrabbed;

                       BaseCamera.StreamGrabber.Start(GrabStrategy.OneByOne, GrabLoop.ProvidedByStreamGrabber);

                        ImageGrabbedAdded = true;
                        
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        object lock1 = new object();
        private void InitBuf(int width, int height)
        {
            lock (lock1)
            {
                if (Datas is null)
                    Datas = new byte[width * height * PixelBytes];
            }
        }



        public void Stop()
        {
            try
            {
                if (BaseCamera?.IsOpen ?? false)


                    // Start the grabbing of images until grabbing is stopped.

                    BaseCamera.StreamGrabber.Stop();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        public void GetBufAddress(out IntPtr[] Datas)
            => GetCurrentFrame(out Datas);

        public unsafe void GetCurrentFrame(out IntPtr[] Datas)
        {
            if (Init() == false)
            {
                Datas = null;
                return;
            }


            fixed (byte* pDatas = &this.Datas[0])
            {
                //byte []Data1 = new byte[this.Datas.Length];
                //     if (Data1 == IntPtr.Zero)
                //        Data1 = Marshal.AllocHGlobal(this.Datas.Length);
                //     Marshal.Copy(this.Datas, 0, Data1, this.Datas.Length);
                long size = (long)frameWidth * frameHeight;
                Datas = new IntPtr[] { (IntPtr)pDatas, (IntPtr)(pDatas + size), (IntPtr)(pDatas + size * 2) };

            }
        }



        public void Dispose()
        {
            BaseCamera.Close();
        }

        bool ImageGrabbedAdded = false;


        int i = 0;
        public void ImageGrabbed(object sender, ImageGrabbedEventArgs e) {

            InitBuf(e.GrabResult.Width, e.GrabResult.Height);
            Converter.Convert(Datas, e.GrabResult);
            Console.Write("grab");
           /* i++;


            GetCurrentFrame(out IntPtr[] Datas1);


            long size = e.GrabResult.Width ;
            size = size * e.GrabResult.Height;
            string type = "byte";


            HImage a = new HImage();
            a.GenImage3Extern(type, e.GrabResult.Width, e.GrabResult.Height, Datas1[0], Datas1[1], Datas1[2], IntPtr.Zero);

            Task.Run(() =>
            {
                HOperatorSet.WriteImage(a, "tiff", 0, "D:\\Image\\" + i + ".tiff");
            });*/
        }

        public void Start(int BufIndex = -1)
        {
            Start(false);
        }

        public IntPtr[] GetBufAddress(int index = -1)
        {
            IntPtr[] Datas =null ;
            GetCurrentFrame(out Datas);
            return Datas;
        }

        public IntPtr[] GetCurrentFrame()
        {
            IntPtr []Datas = new IntPtr[3];
            Datas[0] = (IntPtr)this.Datas[0];
            Datas[1] = (IntPtr)this.Datas[1];
            Datas[2] = (IntPtr)this.Datas[2];

            GetCurrentFrame(  out Datas);
            return Datas;
        }

        public bool SetFeatureValue(GMExpandParamter Param, params object[] value)
        {
            throw new NotImplementedException();
        }

        public object GetFeatureValue(GMExpandParamter Param)
        {
            throw new NotImplementedException();
        }

        public object FunctionCall(GMExpandFunction func, params object[] value)
        {
            throw new NotImplementedException();
        }
    }
}
