using ArenaNET;
using Machine.Core.Enums;
using Machine.Core.Interfaces;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
//using System.Windows.Media;
//using System.Windows.Media.Imaging;

namespace Machine.Core
{
    public class cGM_Arena : ICamera
    {
        enum InfoItem
        {
            Name = 0,
            SerialNumber,
            FrameWidth,
            FrameHeight,
            FrameRate,
            ExposureTime,
            Gain,
            ExternalTriggerMode,
        }
        public GrabModuleType Type => GrabModuleType.Arena;
        public string CCD_Name { get; set; }
        public string CCD_ID { get; set; }
        private double _Gain { get; set; } = 0d;
        public double Gain
        {
            get
            {
                if (device is null)
                    return _Gain;

                return (device.NodeMap.GetNode("Gain") as ArenaNET.IFloat).Value;
            }
            set
            {
                if (device is null)
                {
                    _Gain = value;
                    return;
                }
                SetFloatValue(device.NodeMap, "Gain", value);
            }
        }

        private double _ExposureTime = 200d;    
        public double ExposureTime
        {
            get
            {
                if (device is null)
                    return _ExposureTime;
                
                return (device.NodeMap.GetNode("ExposureTime") as ArenaNET.IFloat).Value;
            }

            set
            {
                if (device is null)
                {
                    _ExposureTime = value;
                    return;
                }
                SetFloatValue(device.NodeMap, "ExposureTime", value);
            }
        }

        private double _FrameRate = 30;
        //Line scan = LineRate, Area scan = FrameRate
        public double Rate
        {
            get
            {
                if (device is null)
                    return _FrameRate;
                return (device.NodeMap.GetNode("AcquisitionFrameRate") as ArenaNET.IFloat).Value;
            }
            set
            {
                return;
            }
        }

        //public int FrameWidth { get {return Convert.ToInt32((device.NodeMap.GetNode("Width") as ArenaNET.IInteger).Value); } set { SetIntValue(device.NodeMap, "Width", value); } }
        //public int FrameHeight { get { return Convert.ToInt32((device.NodeMap.GetNode("Height") as ArenaNET.IInteger).Value); } set { SetIntValue(device.NodeMap, "Height", value); } }
        public int FrameWidth { get; set; } = 1920;
        public int FrameHeight { get; set; } = 1200;

        public int PixelBytes { get; set; } = 3;
        public double PixelWidth { get; set; } = 10;    //unit = um
        public double PixelHeight { get; set; } = 10;

        public string UID { get; set; }
        public string Name { get; set; }
        public string ConfigFileName { get; set; } = @"";

        const ulong IMAGE_TIMEOUT = 2000;

        ISystem system;
        IDevice device;
        IImage image_buff;

        byte[] BufDump;
        IntPtr BufAddress;

        IntPtr GrabCallback;
        ArenaNET.INode GrabEventTimestamp;
        public delegate void GrabEventHandler(int imgid, IntPtr imgptr);
        public event GrabEventHandler OnGrabed;        

        bool IsInitialized = false;
        
        cGM_InitConfig_Arena InitConfig = null;
        public int TriggerCount { get; private set; } = 0;

        public cGM_Arena()
        {
            InitConfig = new cGM_InitConfig_Arena();
        }
        public cGM_Arena(object conf)
        {
            InitConfig = conf as cGM_InitConfig_Arena;
            if (InitConfig == null)
            {
                throw new NotSupportedException($"Not supported config data.(Arena only)");
            }
        }
        public bool Init()
        {            
            if (IsInitialized)
            {
                Console.WriteLine("Camera is initialized.\n");
                return true;
            }
            //if(File.Exists(InitConfig.ConfigFileName))
            //    featureStream.Read(InitConfig.ConfigFileName);  //讀設定檔
            
            system = ArenaNET.Arena.OpenSystem();
            system.UpdateDevices(100);

            if (system.Devices.Count == 0)
            {
                throw new Exception($"No camera connected.");
            }
            if (InitConfig.CameraIndex >= system.Devices.Count)
            {
                throw new IndexOutOfRangeException($"Camera index out of range.(max = {system.Devices.Count - 1}");
            }

            device = system.CreateDevice(system.Devices[InitConfig.CameraIndex]);

            //設置取像參數
            var acquisitionModeNode = (ArenaNET.IEnumeration)device.NodeMap.GetNode("AcquisitionMode");
            String acquisitionModeInitial = acquisitionModeNode.Entry.Symbolic;
            acquisitionModeNode.FromString("Continuous");

            var triggerMode = (ArenaNET.IEnumeration)device.NodeMap.GetNode("TriggerMode");
            String triggerModeInitial = triggerMode.Entry.Symbolic;
            //triggerMode.FromString("Off");  //Internal = "Off",External = "On"

            //Rate = (device.NodeMap.GetNode("AcquisitionFrameRate") as ArenaNET.IFloat).Value;
            //ExposureTime = (device.NodeMap.GetNode("ExposureTime") as ArenaNET.IFloat).Value;
            //Gain = (device.NodeMap.GetNode("Gain") as ArenaNET.IFloat).Value;

            var streamBufferHandlingModeNode = (ArenaNET.IEnumeration)device.TLStreamNodeMap.GetNode("StreamBufferHandlingMode");
            streamBufferHandlingModeNode.FromString("NewestOnly");

            var pixelFormatNode = (ArenaNET.IEnumeration)device.NodeMap.GetNode("PixelFormat");
            String pixelFormatInitial = pixelFormatNode.Entry.Symbolic;
            pixelFormatNode.FromString("RGB8");
            //pixelFormatNode.FromString("BayerRG8");

            //var streamAutoNegotiatePacketSizeNode = (ArenaNET.IBoolean)device.TLStreamNodeMap.GetNode("StreamAutoNegotiatePacketSize");
            //streamAutoNegotiatePacketSizeNode.Value = true;

            //var streamPacketResendEnableNode = (ArenaNET.IBoolean)device.TLStreamNodeMap.GetNode("StreamPacketResendEnable");
            //streamPacketResendEnableNode.Value = true;

            BuildBufferDump(FrameWidth * FrameHeight * PixelBytes);

            ArenaNET.IDeviceInfo deviceInfo = system.Devices[InitConfig.CameraIndex];
            CCD_Name = deviceInfo.ModelName;
            CCD_ID = deviceInfo.SerialNumber;

            //註冊取像事件
            GrabEventTimestamp = device.NodeMap.GetNode("EventTestTimestamp") as ArenaNET.INode;
            if (GrabEventTimestamp == null)
                throw new ArenaNET.GenericException("Event node not found");

            if (device != null)
            {
                IsInitialized = true;
                return true;
            }
            else
            {
                IsInitialized = false;
                return false;
            }
        }
        private bool IsExternalTrigger = false;
        public bool IsGrabing { private set; get; } = false;
        public int BufWidth { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public int BufHeight { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        /// <summary>
        /// 取得指定參數值(建議用var宣告變數來接收回傳值)
        /// </summary>
        /// <param name="info"></param>
        /// <returns></returns>
        private object GetInfomation(InfoItem info)
        {
            if (device is null)
                throw new Exception("");
            IDeviceInfo deviceInfo = system.Devices[InitConfig.CameraIndex];
            switch (info)
            {
                case InfoItem.Name:
                    return deviceInfo.ModelName;

                case InfoItem.SerialNumber:
                    return deviceInfo.SerialNumber;

                case InfoItem.FrameWidth:
                    return Convert.ToInt32((device.NodeMap.GetNode("Width") as ArenaNET.IInteger).Value);

                case InfoItem.FrameHeight:
                    return Convert.ToInt32((device.NodeMap.GetNode("Height") as ArenaNET.IInteger).Value);

                case InfoItem.FrameRate:
                    return (device.NodeMap.GetNode("AcquisitionFrameRate") as ArenaNET.IFloat).Value;

                case InfoItem.ExposureTime:
                    return (device.NodeMap.GetNode("ExposureTime") as ArenaNET.IFloat).Value;

                case InfoItem.Gain:
                    return (device.NodeMap.GetNode("Gain") as ArenaNET.IFloat).Value;

                case InfoItem.ExternalTriggerMode:
                    var triggerMode = (ArenaNET.IEnumeration)device.NodeMap.GetNode("TriggerMode");
                    if (triggerMode.Entry.Symbolic == "On")
                        return true;
                    else
                        return false;

                default:
                    throw new NotSupportedException($"不支援的參數{info.ToString()}");
            }
        }
        private long SetIntValue(ArenaNET.INodeMap nodeMap, String nodeName, long value)
        {
            // get node
            var integerNode = (ArenaNET.IInteger)nodeMap.GetNode(nodeName);

            // Ensure increment
            //    If a node has an increment (all integer nodes & some float
            //    nodes), only multiples of the increment can be set. Ensure this
            //    by dividing and then multiplying by the increment. If a value
            //    is between two increments, this will push it to the lower
            //    value. Most minimum values are divisible by the increment. If
            //    not, this must also be considered in the calculation.
            value = (((value - integerNode.Min) / integerNode.Inc) * integerNode.Inc) + integerNode.Min;

            // Check min/max values
            //    Values must not be less than the minimum or exceed the maximum
            //    value of a node. If a value does so, simply push it within
            //    range.
            if (value < integerNode.Min)
                value = integerNode.Min;

            if (value > integerNode.Max)
                value = integerNode.Max;

            // set value
            integerNode.Value = value;

            // return value for output
            return value;
        }
        private double SetFloatValue(ArenaNET.INodeMap nodeMap, String nodeName, double value)
        {

            var floatNode = (ArenaNET.IFloat)nodeMap.GetNode(nodeName);

            value = (((value - floatNode.Min) / floatNode.Inc) * floatNode.Inc) + floatNode.Min;

            if (value < floatNode.Min)
                value = floatNode.Min;

            if (value > floatNode.Max)
                value = floatNode.Max;

            // set value
            floatNode.Value = value;

            return value;
        }
        private void BuildBufferDump(int byte_size)
        {
            if (BufDump == null || BufDump.Length != byte_size)
            {
                BufDump = new byte[byte_size];
                unsafe
                {
                    fixed (byte* ptr = &BufDump[0])
                        BufAddress = (IntPtr)ptr;
                }
            }
        }
        //
        private void GrabHandler(ArenaNET.INode node)
        {            
            OnGrabed?.Invoke(TriggerCount++, GetBufAddress());
        }
        public void Start(bool IsInternal = true)
        {
            if (IsGrabing == false)
            {
                device.StartStream();
                IsGrabing = true;
                if ((bool)GetInfomation(InfoItem.ExternalTriggerMode) == true)
                {
                    GrabCallback = GrabEventTimestamp.RegisterCallback(new ArenaNET.INode.del(GrabHandler));
                    TriggerCount = 0;
                }
            }
        }


        public void Stop()
        {
            if (IsGrabing == true)
            {
                device.StopStream();
                GrabEventTimestamp.DeregisterCallback(GrabCallback);
                IsGrabing = false;

            }
        }

        public void ClearCalibration()
        {


        }

        public void FPNSet()
        {


        }

        int NowPRNUSet = 0;
        public void SelectPRNUSet(int index)
        {

        }

        public void SavePRNUSet(int index = -1)
        {
            if (index == -1)
            {

            }
            else
            {

            }

        }
        public void PRNUSet(/*uint target = 200*/)
        {


        }
        private void GrabingCallbackEven()
        {

        }

        public void GetBufAddress(out IntPtr[] Datas)   //取用相機輸出的圖形資料位址(bmp)
        {
            //平均取像時間約15ms
            //1. PixelBytes 必須設為 4
            image_buff = device.GetImage(IMAGE_TIMEOUT);

            Rectangle rect = new Rectangle(0, 0, FrameWidth, FrameHeight);
            System.Drawing.Imaging.BitmapData bmpData = image_buff.Bitmap.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, image_buff.Bitmap.PixelFormat);

            int buf_size = image_buff.Bitmap.Width * image_buff.Bitmap.Height * PixelBytes;
            if (BufDump.Length != buf_size)
                BuildBufferDump(buf_size);

            Marshal.Copy(bmpData.Scan0, BufDump, 0, buf_size); //複製一份副本來回傳，避免RequeueBuffer後資料被清除

            image_buff.Bitmap.UnlockBits(bmpData);

            if (image_buff != null)
                device.RequeueBuffer(image_buff);

            Datas = new IntPtr[] { BufAddress };
        }
        public void GetBufAddress(out IntPtr R, out IntPtr G, out IntPtr B)
        {
            //int size = FrameWidth * BufHeight;
            //R = BufAddress;
            //G = BufAddress + size;
            //B = BufAddress + size * 2;
            throw new NotImplementedException();
        }

        public IntPtr GetBufAddress()   //直接取用Buffer資料位址
        {
            image_buff = device.GetImage(IMAGE_TIMEOUT);

            //直接用Buffer記憶體位置取像，平均取像時間約6ms 
            //1. 由於記憶體會被鎖住所以無法直接轉為Bitmap使用
            //2. 相機 PixelFormat 必須設為 RGB8
            //3. 有可能產生Buffer被清除後的空值異常
            //4. PixelBytes 必須設為 3            
            //unsafe
            //{
            //    fixed (byte* ptr = &image_buff.DataArray[0])
            //        BufAddress = (IntPtr)ptr;
            //}
            //if (image_buff != null)
            //    device.RequeueBuffer(image_buff);
            //return BufAddress;

            
            //同上，但是先複製一份Buffer再回傳複製資料的位置以避免Buffer空值異常，平均取像時間約14ms
            //1. 相機 PixelFormat 必須設為 RGB8
            //2. PixelBytes 必須設為 3            
            int buf_size = image_buff.Bitmap.Width * image_buff.Bitmap.Height * PixelBytes;
            if (BufDump.Length != buf_size)
                BuildBufferDump(buf_size);
            //Buffer.BlockCopy(image_buff.DataArray, 0, BufDump, 0, buf_size);
            Marshal.Copy(image_buff.DataArray, 0, BufAddress, buf_size); //複製一份副本來回傳，避免RequeueBuffer後資料被清除 
            if (image_buff != null)
                device.RequeueBuffer(image_buff);

            return BufAddress;            
        }

        public void GetCurrentFrame(out IntPtr[] Datas)
        {
            //GetBufAddress(out Datas);
            Datas = new IntPtr[] { GetBufAddress() };

            //GetBufAddress();
            //int size = FrameWidth * FrameHeight;
            //Datas = new IntPtr[] { BufAddress, BufAddress + size, BufAddress + size * 2 };

        }
        public void GetCurrentFrame(out IntPtr R, out IntPtr G, out IntPtr B)
        {
            //int size = FrameWidth * BufHeight;
            //int loc = FrameWidth * Math.Max(0, NowLine - FrameHeight);

            //R = BufAddress + loc;
            //G = BufAddress + loc + size;
            //B = BufAddress + loc + size * 2;

            throw new NotImplementedException();
        }

        public void Dispose()
        {
            if (IsInitialized)
            {
                Stop();
                system.DestroyDevice(device);
                ArenaNET.Arena.CloseSystem(system);
            }
        }

        public void Start(int BufIndex = -1)
        {
            throw new NotImplementedException();
        }

        public nint[] GetBufAddress(int index = -1)
        {
            throw new NotImplementedException();
        }

        public nint[] GetCurrentFrame()
        {
            throw new NotImplementedException();
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
