using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using DALSA.SaperaLT.SapClassBasic;
using Machine.Core.Enums;
using Machine.Core.Interfaces;
using Newtonsoft.Json;
using Windows.Foundation;
using static DALSA.SaperaLT.SapClassBasic.SapAcquisition;
using Windows.Foundation;
//using Rect = System.Windows.Rect;

namespace Machine.Core
{
    public class cGM_Dalsa_0 : ICamera
    {
        public GrabModuleType Type => GrabModuleType.Dalsa_0;
        public string CCD_Name { get; set; }
        public string CCD_ID { get; set; }
        public double Gain { get; set; }
        public double ExposureTime { get; set; } = 20;
        public double Rate { get; set; } = 35000d;
        public int FrameWidth { get; set; } = 16352;
        public int FrameHeight { get; set; } = 1000;
        public int BufWidth { get; set; } = 16352;
        public int BufHeight { get; set; } = 1000;
        public double PixelWidth { get; set; } = 0.01;
        public double PixelHeight { get; set; } = 0.01;
        public string UID { get; set; }
        public string Name { get; set; }
        public string ConfigFileName { get; set; } = @"C:\Windows\System32\Setting_Vtest\Dalsa_.ccf";
        public int PixelBytes { get; private set; } = 3;

        public const int TotalGrabCount = 1;

        IntPtr[] BufAddress = new IntPtr[TotalGrabCount];

        bool IsInitialized = false;

        SapAcquisition m_Acq;

        SapAcqDevice m_AcqDevice;

        SapBuffer m_Buffer;

        SapTransfer m_Xfer;

        SapLocation m_location;

        int serverIndex = 01;

        int ShaftEncoderDrop = 0;

        int Binning;

        public bool IsGrabing;

        bool IsInternal_Status;

        private object Lock = new object();
        public T Protect<T>(Func<T> Function)
        {
            try
            {
                while (Monitor.IsEntered(Lock))
                    Thread.Sleep(100);

                Monitor.Enter(Lock);

                Init();
                if (IsInitialized)
                    return Function();
                else
                    return default(T);
            }
            finally
            {
                Monitor.Exit(Lock);
            }
        }
        public void Protect(Action Action)
        {
            try
            {
                while (Monitor.IsEntered(Lock))
                    Thread.Sleep(100);

                Monitor.Enter(Lock);

                Init();
                if (IsInitialized)
                    Action();
            }
            finally
            {
                Monitor.Exit(Lock);

            }


        }
        public void PrepareStart(bool IsInternal = true, FlipMode flipMode = FlipMode.None, int binning = 1)
        {
            //ex line rate drop
            /*
             * 
             ,int ExposureTime,int LineRate,in SHAFT_ENCODER_DROP


            success = m_AcqDevice.SetFeatureValue("ExposureTime", ExposureTime);
            success = m_AcqDevice.SetFeatureValue("SensorScanDirection", "Forward"); //加入掃描方向 
                                                                                     //success = m_AcqDevice.SetFeatureValue("Scandirection", "Forward"); 加入 BUF 最大

            success = m_AcqDevice.SetFeatureValue("AcquisitionLineRate", Rate);

                 m_Acq.SetParameter(SapAcquisition.Prm.SHAFT_ENCODER_DROP , 29, true);


             */
            Protect(() =>
            {

                if (IsGrabing == false)
                {
                    if (IsInternal != IsInternal_Status)
                    {
                        string Value = IsInternal ? "Internal" : "External";
                        bool a = m_AcqDevice.SetFeatureValue("TriggerMode", Value);
                        IsInternal_Status = IsInternal;
                    }
                    //NowLine = 0;
                    SapXferPair.FlipMode mode = SapXferPair.FlipMode.None;// m_Xfer.Pairs[0].Flip;

                    if (flipMode == FlipMode.None)
                        mode = SapXferPair.FlipMode.None;
                    else if (flipMode == FlipMode.Horizontal)
                        mode = SapXferPair.FlipMode.Horizontal;
                    else if (flipMode == FlipMode.Vertical)
                        mode = SapXferPair.FlipMode.Vertical;
                    // m_AcqDevice.GetFeatureValue("BinningHorizontal", out Binning);

                    if (mode != m_Xfer.Pairs[0].Flip || Binning != binning)
                    {
                        m_Xfer.Disconnect();

                        m_Xfer.Pairs[0].Flip = mode;

                        if (Binning > binning)
                        {
                            m_AcqDevice.SetFeatureValue("ExposureTime", ExposureTime * binning);
                            m_AcqDevice.SetFeatureValue("AcquisitionLineRate", Rate / binning);

                        }
                        else
                        {
                            m_AcqDevice.SetFeatureValue("AcquisitionLineRate", Rate / binning);
                            m_AcqDevice.SetFeatureValue("ExposureTime", ExposureTime * binning);
                        }
                        SetFeatureValue(GMExpandParamter.SHAFT_ENCODER_DROP, (ShaftEncoderDrop + 1) * binning - 1, true);
                        // m_Acq.SetParameter(SapAcquisition.Prm.SHAFT_ENCODER_DROP, (ShaftEncoderDrop + 1) * binning - 1, true);
                        Binning = binning;
                        /*
                                                m_AcqDevice.SetFeatureValue("BinningHorizontal", binning);
                                                m_AcqDevice.SetFeatureValue("BinningVertical", binning);
                                                */

                        m_Xfer.Connect();

                    }

                    /*

                    if (flipMode == FlipMode.None && mode != SapXferPair.FlipMode.None)
                    {


                        //m_Acq.Flip = SapAcquisition.FlipMode.None;

                        m_Xfer.Disconnect();
                        m_Xfer.Pairs[0].Flip = SapXferPair.FlipMode.None;
                        m_Xfer.Connect();
                    }
                    else if (flipMode == FlipMode.Horizontal && mode != SapXferPair.FlipMode.Horizontal)
                    {
                        m_Xfer.Disconnect();
                        m_Xfer.Pairs[0].Flip = SapXferPair.FlipMode.Horizontal;
                        m_Xfer.Connect();

                    }
                    else if (flipMode == FlipMode.Vertical && mode != SapXferPair.FlipMode.Vertical)
                    {


                        m_Xfer.Disconnect();
                        m_Xfer.Pairs[0].Flip = SapXferPair.FlipMode.Vertical;
                        //m_Acq.Flip = SapAcquisition.FlipMode.Vertical;
                        m_Xfer.Connect();


                    }*/
                    //  NowLine = 0;
                    //m_Xfer.XferNotify += new SapXferNotifyHandler(xfer_XferNotify);




                    //m_Xfer.Grab();
                    //IsGrabing = true;
                }
            });

        }


        int NowPRNUSet = 0;
        public void SelectPRNUSet(int index)
        {
            Protect(() =>
            {
                String Set = "Factory";
                if (index > 8 || index < 0)
                    throw new NotSupportedException("PRNUSet Load = 0 ~ 8 ");

                if (index != 0)
                    Set = "UserSet" + index;

                if (NowPRNUSet != index)
                {
                    bool a = m_AcqDevice.SetFeatureValue("flatfieldCorrectionCurrentActiveSet", Set);
                    bool b = m_AcqDevice.SetFeatureValue("flatfieldCalibrationLoad", true);
                    NowPRNUSet = index;
                }
            });
        }
        public bool CurrentPRNUtoFile(string FileName)
        {
            return m_AcqDevice.ReadFile("Cur_PRNU", FileName);
        }
        public bool CurrentPRNUfromFile(string FileName)
        {
            return m_AcqDevice.WriteFile(FileName, "Cur_PRNU");
        }
        public double GetGain(ChannelType channel)
        {
            double Gain = 0;
            if (channel == ChannelType.R)
            {
                m_AcqDevice.SetFeatureValue("GainSelector", "Red");
                m_AcqDevice.GetFeatureValue("Gain", out Gain);
            }
            else if (channel == ChannelType.G)
            {
                m_AcqDevice.SetFeatureValue("GainSelector", "Green");
                m_AcqDevice.GetFeatureValue("Gain", out Gain);
            }
            else if (channel == ChannelType.B)
            {
                m_AcqDevice.SetFeatureValue("GainSelector", "Blue");
                m_AcqDevice.GetFeatureValue("Gain", out Gain);
            }
            else if (channel == ChannelType.System)
            {
                m_AcqDevice.SetFeatureValue("GainSelector", "System");
                m_AcqDevice.GetFeatureValue("Gain", out Gain);
            }
            return Gain;
        }
        public void SetDirection(ScanDirection scanDirection)
        {


            if (scanDirection == ScanDirection.Forward)
                m_AcqDevice.SetFeatureValue("SensorScanDirection", "Forward");
            else
                m_AcqDevice.SetFeatureValue("SensorScanDirection", "Reverse");
            m_scanDirection = scanDirection;

        }
        public void SetGain(ChannelType channel, double Gain = 1)
        {

            if (channel == ChannelType.R)
            {
                m_AcqDevice.SetFeatureValue("GainSelector", "Red");
                m_AcqDevice.SetFeatureValue("Gain", Gain);
            }
            else if (channel == ChannelType.G)
            {
                m_AcqDevice.SetFeatureValue("GainSelector", "Green");
                m_AcqDevice.SetFeatureValue("Gain", Gain);
            }
            else if (channel == ChannelType.B)
            {
                m_AcqDevice.SetFeatureValue("GainSelector", "Blue");
                m_AcqDevice.SetFeatureValue("Gain", Gain);
            }
            else if (channel == ChannelType.System)
            {
                bool a = m_AcqDevice.SetFeatureValue("GainSelector", "System");
                a = m_AcqDevice.SetFeatureValue("Gain", Gain);
            }
        }
        public void SavePRNUSet(int index = -1)
        {
            Protect(() =>
            {
                if (index == -1)
                {
                    if (NowPRNUSet != 0)
                    {
                        String Set = "UserSet" + NowPRNUSet;
                        m_AcqDevice.SetFeatureValue("flatfieldCorrectionCurrentActiveSet", Set);

                        m_AcqDevice.SetFeatureValue("flatfieldCalibrationSave", true);
                    }
                }
                else
                {
                    String Set = "Factory";
                    if (index > 8 || index < 1)
                        throw new NotSupportedException("PRNUSet Save = 1 ~ 8 ");

                    if (index != 0)
                    {
                        Set = "UserSet" + index;
                        bool a = m_AcqDevice.SetFeatureValue("flatfieldCorrectionCurrentActiveSet", Set);
                        bool b = m_AcqDevice.SetFeatureValue("flatfieldCalibrationSave", true);
                    }
                }
            });

        }
        public void PRNUSet(uint target = 150)
        {
            Protect(() =>
            {

                /*
                bool aaa = m_AcqDevice.SetFeatureValue("flatfieldCorrectionAlgorithm", "Target");

                aaa = m_AcqDevice.SetFeatureValue("flatfieldCalibrationTarget", target);

                aaa = m_AcqDevice.SetFeatureValue("flatfieldCalibrationPRNU", true);

                */
                /*
                aaa = m_AcqDevice.SetFeatureValue("flatfieldCalibrationSave", true);
                */
                //   m_AcqDevice.SetFeatureValue("flatfieldCalibrationPRNU",true);
                /// m_AcqDevice.SetFeatureValue("BalanceWhiteAuto", true);
                //  pFlatField.Dispose();

                //bool aaa = m_AcqDevice.SetFeatureValue("flatfieldCorrectionAlgorithm", "Peak");
                // m_AcqDevice.SetFeatureValue("ExposureTime", ExposureTime/2);

                bool aaa = m_AcqDevice.SetFeatureValue("flatfieldCorrectionAlgorithm", "PeakFilter");

                bool bbb = m_AcqDevice.SetFeatureValue("flatfieldCalibrationPRNU", true);
                m_AcqDevice.SetFeatureValue("BalanceWhiteAuto", true);
                // m_AcqDevice.SetFeatureValue("ExposureTime", ExposureTime );
            });

        }
        public object FunctionCall(GMExpandFunction func, params object[] value)
        {
            bool bSuccess = true;
            int Length = 0;
            try
            {
                switch (func)
                {
                    case GMExpandFunction.PrepareStart:
                        Length = 3;
                        PrepareStart((bool)value[0], (FlipMode)value[1], (int)value[2]);
                        break;
                    case GMExpandFunction.ClearCalibration:
                        ClearCalibration();
                        break;
                    case GMExpandFunction.FPNSet:
                        FPNSet();
                        break;
                    case GMExpandFunction.SelectPRNUSet:
                        Length = 1;
                        SelectPRNUSet((int)value[0]);
                        break;
                    case GMExpandFunction.GetGain:
                        Length = 1;
                        return GetGain((ChannelType)value[0]);
                    case GMExpandFunction.SetDirection:
                        {
                            Length = 1;
                            SetDirection((ScanDirection)value[0]);
                        }
                        break;
                    case GMExpandFunction.SetGain:
                        Length = 1;
                        SetGain((ChannelType)value[0], (double)value[1]);
                        break;
                    case GMExpandFunction.SavePRNUSet:
                        Length = 1;
                        SavePRNUSet((int)value[0]);
                        break;
                    case GMExpandFunction.PRNUSet:
                        
                        Length = 1;
                        PRNUSet((uint)((int)value[0]));
                        break;
                    case GMExpandFunction.GetScanedRect:
                        Length = 1;
                        return GetScanedRect((ScanDirection)value[0]);

                    default:
                        throw new Exception($" Error-{GetType().Name}-{System.Reflection.MethodBase.GetCurrentMethod().Name} : func is invalid");
                }
            }
            catch (IndexOutOfRangeException ex)
            {
                throw new Exception($"{func} FunctionCall Param Length is {Length} , input is {value.Length}");
            }
            catch (InvalidCastException ex)
            {
                throw new Exception($"{func} 參數轉型錯誤");
            }
            catch (Exception ex)
            {
                throw new Exception($"{ex.Message }");
            }
            return bSuccess;
        }
        public Rect GetScanedRect(ScanDirection direction)
        {

            Rect rect = new Rect();
            if (direction == ScanDirection.Forward)
            {
                rect.X = 0;
                rect.Y = 0;
                rect.Width = BufWidth;
                rect.Height = NowLine;

            }
            else
            {
                rect.X = 0;
                rect.Y = BufHeight - NowLine;
                rect.Width = BufWidth;
                rect.Height = NowLine;

            }

            return rect;
        }
        public void FPNSet()
        {
            Protect(() =>
            {
                bool a = m_AcqDevice.SetFeatureValue("flatfieldCalibrationFPN", true);
            });

        }
        public unsafe IntPtr[] GetBufAddress(int index = -1)
        {
            if (index == -1)
                index = GetCurrentBufID();

            IntPtr[] Datas = new IntPtr[3];
            long size = FrameWidth ;
            size = size * BufHeight;
            Datas[0] = BufAddress[index];
            Datas[1] =(IntPtr  )((byte*)BufAddress[index] + size);
            Datas[2] = (IntPtr)((byte*)BufAddress[index] + size*2);
            return Datas;

        }

        public unsafe IntPtr[] GetCurrentFrame()
        {/*
            int index = GetCurrentBufID();


            int size = FrameWidth * BufHeight;
            int loc = FrameWidth * Math.Max(0, NowLine - FrameHeight);
            IntPtr[] Datas = new IntPtr[3];
            Datas[0] = BufAddress[index] + loc;
            Datas[1] = BufAddress[index] + loc + size;
            Datas[2] = BufAddress[index] + loc + size * 2;
            return Datas;
            */

            int index = GetCurrentBufID();

            IntPtr[] Datas = new IntPtr[3];
            long size = FrameWidth;
            size = size * BufHeight;

            long loc = FrameWidth * Math.Max(0, NowLine - FrameHeight);


            Datas[0] = BufAddress[index];
            Datas[1] = (IntPtr)((byte*)BufAddress[index] + size);
            Datas[2] = (IntPtr)((byte*)BufAddress[index] + size * 2);
            return Datas;



        }



        public bool Init()
        {


            if (IsInitialized)
                return true;
            SapManager.ServerEventType = SapManager.EventType.ServerNew |
                                         SapManager.EventType.ServerAccessible |
                                         SapManager.EventType.ServerNotAccessible;

            int serverCount = SapManager.GetServerCount();

            if (serverCount == 0)//No server
                return false;


            if (SapManager.GetResourceCount(serverIndex, SapManager.ResourceType.AcqDevice) == 0)
                return false;
            string name = SapManager.GetServerName(serverIndex);


            m_location = new SapLocation(name, 1);// 1是彩色    0是黑白


            if (SapManager.GetResourceCount(name, SapManager.ResourceType.Acq) > 0)
            {
                m_Acq = new SapAcquisition(m_location, ConfigFileName);
                m_Acq.Create();

                SetFeatureValue(GMExpandParamter.CROP_WIDTH, BufWidth, true);
                SetFeatureValue(GMExpandParamter.CROP_HEIGHT, BufHeight, true);

                // m_Acq.SetParameter(SapAcquisition.Prm.CROP_WIDTH, BufWidth, true);

                //m_Acq.SetParameter(SapAcquisition.Prm.CROP_HEIGHT, BufHeight, true);

                //m_Acq.SetParameter(SapAcquisition.Prm.SHAFT_ENCODER_DROP, 49,true);
                //m_Acq.SetParameter(SapAcquisition.Prm.CROP_HEIGHT, 140000, true);
                //m_Acq.SetParameter(SapAcquisition.Prm.SHAFT_ENCODER_DROP , 29, true);
                //int ShaftEncoderDrop;
                //m_Acq.GetParameter(SapAcquisition.Prm.SHAFT_ENCODER_DROP, out ShaftEncoderDrop);   
                ShaftEncoderDrop = (int)GetFeatureValue(GMExpandParamter.SHAFT_ENCODER_DROP);

                //m_Acq.GetParameter(SapAcquisition.Prm.SHAFT_ENCODER_DROP, out ShaftEncoderDrop);

                m_Buffer = new SapBuffer(TotalGrabCount, m_Acq, SapBuffer.MemoryType.ScatterGather);

                m_Xfer = new SapAcqToBuf(m_Acq, m_Buffer);



            }

            GMExpandParamter.ExposureTime.ToString();

            SapLocation loc2 = new SapLocation(name, 0);
            m_AcqDevice = new SapAcqDevice(loc2, false);

            if (!m_AcqDevice.Create())
                throw new Exception("GM_Dasal1 : Error during SapBuffer creation!\n");

            SetFeatureValue(GMExpandParamter.ExposureTime, ExposureTime);

            //   success = m_AcqDevice.SetFeatureValue("ExposureTime", ExposureTime);


            SetFeatureValue(GMExpandParamter.sensorScanDirection, "Forward"); //加入掃描方向          

            // success = m_AcqDevice.SetFeatureValue("SensorScanDirection", "Forward"); //加入掃描方向          

            SetFeatureValue(GMExpandParamter.AcquisitionLineRate, Rate);

            //success = m_AcqDevice.SetFeatureValue("AcquisitionLineRate", Rate);
            
            SetFeatureValue(GMExpandParamter.TriggerMode, false);

            //  success = m_AcqDevice.SetFeatureValue("TriggerMode", false);

            SetFeatureValue(GMExpandParamter.GainSelector, "System");


            //m_AcqDevice.GetFeatureValue("ROI ", false);  60000

            /*
            for (int i = 1; i < 8; i++)
            {
               m_AcqDevice.SetFeatureValue("flatfieldCorrectionCurrentActiveSet", $"UserSet{i}");
               m_AcqDevice.SetFeatureValue("flatfieldCalibrationLoad", $"UserSet{i}");

               m_AcqDevice.ReadFile("Cur_PRNU", $"PRNU{i}");

             }

            m_AcqDevice.ReadFile("User_PRNU", "PRNU");

            for (int i = 1; i < 8; i++)
            {
               m_AcqDevice.SetFeatureValue("flatfieldCorrectionCurrentActiveSet", $"UserSet{i}");
               m_AcqDevice.SetFeatureValue("flatfieldCalibrationLoad", $"UserSet{i}");

               m_AcqDevice.WriteFile( $"PRNU{i}",$"User_PRNU");

            }
            */

            m_Xfer.Pairs[0].EventType = SapXferPair.XferEventType.EndOfNLines + (FrameHeight);

            if (!m_Buffer.Create())
            {
                Console.WriteLine("Error during SapBuffer creation!\n");
                throw new Exception("GM_Dasal1 : Error during SapBuffer creation!\n");

            }
            else
            {
                BufAddress = new IntPtr[TotalGrabCount];
                BufHeight = m_Buffer.Height;
                FrameWidth = m_Buffer.Width;
                BufWidth = FrameWidth;
                for (int i = 0; i < TotalGrabCount; i++)
                    m_Buffer.GetAddress(i, out BufAddress[i]);
            }

            if (!m_Xfer.Create())
            {
                Console.WriteLine("Error during SapTransfer creation!\n");
                throw new Exception("GM_Dasal  Error during SapTransfer creation!\n");

            }

            IsInitialized = true;

            return true;
        }

        public bool SetFeatureValue(GMExpandParamter Param, params object[] value)
        {
            bool bSuccess = false;

            switch (Param)
            {
                case GMExpandParamter.CROP_HEIGHT:
                    bSuccess = m_Acq.SetParameter(SapAcquisition.Prm.CROP_HEIGHT, (int)value[0], (bool)value[1]);
                    break;
                case GMExpandParamter.CROP_WIDTH:
                    bSuccess = m_Acq.SetParameter(SapAcquisition.Prm.CROP_WIDTH, (int)value[0], (bool)value[1]);
                    break;
                case GMExpandParamter.SHAFT_ENCODER_DROP:
                    bSuccess = m_Acq.SetParameter(SapAcquisition.Prm.SHAFT_ENCODER_DROP, (int)value[0], (bool)value[1]);
                    break;
                case GMExpandParamter.AcquisitionLineRate:
                case GMExpandParamter.ExposureTime:
                    bSuccess = m_AcqDevice.SetFeatureValue(Param.ToString(), (double)value[0]);
                    break;
                //string
                case GMExpandParamter.GainSelector:
                case GMExpandParamter.sensorScanDirection:
                    bSuccess = m_AcqDevice.SetFeatureValue(Param.ToString(), (string)value[0]);
                    break;
                //bool
                case GMExpandParamter.TriggerMode:
                    {
                        
                        string Value;
                        if ((bool)value[0])
                        {
                            Value = "External";
                            IsInternal_Status = false;
                        }
                        else
                        {
                            Value = "Internal";
                            IsInternal_Status = true;
                        }
                        bSuccess = m_AcqDevice.SetFeatureValue("TriggerMode", Value);
                       

                       
                    }
                    break;
                default:
                    throw new Exception($" Error-{GetType().Name}-{System.Reflection.MethodBase.GetCurrentMethod().Name} : Param is invalid");
            }
            return bSuccess;
        }



        int NowLine = 0;
        private ScanDirection m_scanDirection;

        void xfer_XferNotify(object sender, SapXferNotifyEventArgs args)
        {
            Protect(() =>
            {

                if (NowLine + FrameHeight > BufHeight)
                    NowLine = FrameHeight;
                else
                    NowLine = NowLine + FrameHeight;
            }
           );
        }


        public void Start(int BufIndex = -1)
        {

            Protect(() =>
            {

                if (IsGrabing == false)
                {

                    NowLine = 0;
                    m_Xfer.XferNotify += new SapXferNotifyHandler(xfer_XferNotify);
                    if (BufIndex != -1)
                        m_Xfer.Select(0, 0, BufIndex);


                    m_Xfer.Grab();

                    IsGrabing = true;
                }
            });

            // GrabBufIndex = CurrentBufIndex;
        }

        public void Stop()
        {
            Protect(() =>
            {

                if (IsGrabing == true)
                {
                    // Thread.Sleep(2000);
                    // bool a = m_Xfer.Freeze();
                    //m_Xfer.Wait(200);// 改200 
                    // onFrameEnd();
                    m_Xfer.Abort();
                    m_Xfer.XferNotify -= new SapXferNotifyHandler(xfer_XferNotify);
                    IsGrabing = false;
                }
            });
        }
        public int GetCurrentBufID()
        {
            return m_Buffer.Index;
        }
        public object GetFeatureValue(GMExpandParamter Param)
        {
            switch (Param)
            {
                case GMExpandParamter.CROP_HEIGHT:
                    {
                        int value;
                        if (m_Acq.GetParameter(SapAcquisition.Prm.CROP_HEIGHT, out value))
                            return value;
                    }
                    break;
                case GMExpandParamter.CROP_WIDTH:
                    {
                        int value;
                        if (m_Acq.GetParameter(SapAcquisition.Prm.CROP_WIDTH, out value))
                            return value;
                    }
                    break;
                case GMExpandParamter.SHAFT_ENCODER_DROP:
                    {
                        int value;
                        if (m_Acq.GetParameter(SapAcquisition.Prm.SHAFT_ENCODER_DROP, out value))
                            return value;
                    }
                    break;
                case GMExpandParamter.AcquisitionLineRate:
                case GMExpandParamter.ExposureTime:
                    {
                        double value = 0;
                        if (m_AcqDevice.GetFeatureValue(Param.ToString(), out value))
                            return value;

                    }
                    break;
                //string
                case GMExpandParamter.GainSelector:
                    {
 

                    }
             
                    break;
                case GMExpandParamter.sensorScanDirection:
                    {
                        string value;
                        if (m_AcqDevice.GetFeatureValue(Param.ToString(), out value))
                        {

                            if (value == "Forward")
                                return ScanDirection.Forward;
                            if (value == "Reverse")
                                return ScanDirection.Reverse;
                        }

                    }
                    break;
                //bool
                case GMExpandParamter.TriggerMode:
                    {
                        bool value;
                        if (m_AcqDevice.GetFeatureValue(Param.ToString(), out value))
                            return value;

                    }
                    break;
                default:
                    throw new Exception($" Error-{GetType().Name}-{System.Reflection.MethodBase.GetCurrentMethod().Name} : Param is invalid");
            }
            return null;
        }
        public void ClearCalibration()
        {
            Protect(() =>
            {
                //  bool bok = m_AcqDevice.SetFeatureValue("flatfieldCalibrationClearCoefficient", true);
                bool bok = m_AcqDevice.SetFeatureValue("Initialize", true);
            });

        }


    }
}
