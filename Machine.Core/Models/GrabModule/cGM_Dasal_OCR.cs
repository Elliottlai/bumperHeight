using DALSA.SaperaLT.SapClassBasic;
using Machine.Core.Enums;
using Machine.Core.Interfaces;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Machine.Core
{
  public class cGM_Dasal_OCR : ICamera
  {
    public GrabModuleType Type => GrabModuleType.Dasal_OCR;


    public string CCD_Name { get; set; }

    public string CCD_ID { get; set; }

    public double Gain { get; set; }
    public double ExposureTime { get; set; } = 22;
    public double Rate { get; set; } = 35000d;
    public int FrameWidth { get; set; } = 16352;
    public int FrameHeight { get; set; } = 1000;

    public int BufHeight { get; set; } = 60000;
    public double PixelWidth { get; set; } = 10;
    public double PixelHeight { get; set; } = 10;



    public string UID { get; set; }
    public string Name { get; set; }
    public string ConfigFileName { get; set; } = @"C:\Windows\System32\Setting_Vtest\T_PX-HM-16K06X-00-R_Default_Default.ccf";
    public int PixelBytes { get; private set; } = 1;

    SapAcquisition m_Acq;

    SapAcqDevice m_AcqDevice;

    SapBuffer m_Buffer;

    SapTransfer m_Xfer;

    SapLocation m_location;

    IntPtr BufAddress;
    bool IsInitialized = false;

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
      int serverIndex = 01;
      if (SapManager.GetResourceCount(serverIndex, SapManager.ResourceType.AcqDevice) == 0)
        return false;
      string name = SapManager.GetServerName(serverIndex);


      m_location = new SapLocation(name, 0);// 1是彩色    0是黑白


      if (SapManager.GetResourceCount(name, SapManager.ResourceType.Acq) > 0)
      {
        m_Acq = new SapAcquisition(m_location, ConfigFileName);

        m_Buffer = new SapBuffer(1, m_Acq, SapBuffer.MemoryType.ScatterGather);



        m_Xfer = new SapAcqToBuf(m_Acq, m_Buffer);

        m_Acq.Create();

      }



      SapLocation loc2 = new SapLocation(name, 0);
      m_AcqDevice = new SapAcqDevice(loc2, false);


      bool success = m_AcqDevice.Create();


      success = m_AcqDevice.SetFeatureValue("ExposureTime", ExposureTime);
      success = m_AcqDevice.SetFeatureValue("SensorScanDirection", "Forward"); //加入掃描方向 
                                                                               //success = m_AcqDevice.SetFeatureValue("Scandirection", "Forward"); 加入 BUF 最大

      success = m_AcqDevice.SetFeatureValue("AcquisitionLineRate", Rate);
      success = m_AcqDevice.SetFeatureValue("TriggerMode", false);
      /*
         success = m_AcqDevice.SetFeatureValue("GainSelector", "System");

         success = m_AcqDevice.SetFeatureValue("Gain", 2.0);*/

      //m_AcqDevice.GetFeatureValue("ROI ", false);  60000

      m_Xfer.Pairs[0].EventType = SapXferPair.XferEventType.EndOfNLines + (FrameHeight);

      if (!m_Buffer.Create())
      {
        Console.WriteLine("Error during SapBuffer creation!\n");
        //throw new Exception("GM_Dasal1 : Error during SapBuffer creation!\n");
        return false;
      }
      else
      {
        BufHeight = m_Buffer.Height;
        FrameWidth = m_Buffer.Width;
        m_Buffer.GetAddress(out BufAddress);
      }

      if (!m_Xfer.Create())
      {
        Console.WriteLine("Error during SapTransfer creation!\n");
        //throw new Exception("GM_Dasal1 Error during SapTransfer creation!\n");
        return false;
      }

      IsInitialized = true;
      return true;
    }
    private bool? IsInternal_Status = null;
    public bool IsGrabing { private set; get; } = false;
        public int BufWidth { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public void Start(bool IsInternal = true)
    {

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
          m_Xfer.XferNotify += new SapXferNotifyHandler(xfer_XferNotify);
                //m_Xfer.Select(0, 0, 0);
                NowLine = 0;
          m_Xfer.Grab();
          IsGrabing = true;
        }
      });
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

    public void ClearCalibration()
    {
      Protect(() =>
      {
              //  bool bok = m_AcqDevice.SetFeatureValue("flatfieldCalibrationClearCoefficient", true);
              bool bok = m_AcqDevice.SetFeatureValue("Initialize", true);
      });

    }

    public void FPNSet()
    {
      Protect(() =>
      {
        m_AcqDevice.SetFeatureValue("flatfieldCalibrationFPN", true);
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
          m_AcqDevice.SetFeatureValue("flatfieldCorrectionCurrentActiveSet", Set);
          m_AcqDevice.SetFeatureValue("flatfieldCalibrationLoad", Set);
          NowPRNUSet = index;
        }
      });
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

            m_AcqDevice.SetFeatureValue("flatfieldCalibrationSave", Set);
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
            m_AcqDevice.SetFeatureValue("flatfieldCorrectionCurrentActiveSet", Set);
            m_AcqDevice.SetFeatureValue("flatfieldCalibrationSave", Set);
          }
        }
      });

    }
    public void PRNUSet(/*uint target = 200*/)
    {
      Protect(() =>
      {

              /*
              bool aaa = m_AcqDevice.SetFeatureValue("flatfieldCorrectionAlgorithm", "Target");

              aaa = m_AcqDevice.SetFeatureValue("flatfieldCalibrationTarget", target);

              aaa = m_AcqDevice.SetFeatureValue("flatfieldCalibrationPRNU", true);

              aaa = m_AcqDevice.SetFeatureValue("flatfieldCalibrationSave", true);
              */
              //   m_AcqDevice.SetFeatureValue("flatfieldCalibrationPRNU",true);
              /// m_AcqDevice.SetFeatureValue("BalanceWhiteAuto", true);
              //  pFlatField.Dispose();

              bool aaa = m_AcqDevice.SetFeatureValue("flatfieldCorrectionAlgorithm", "Peak");
              // bool aaa = m_AcqDevice.SetFeatureValue("flatfieldCorrectionAlgorithm", "Peak, Image Filtered");

              bool bbb = m_AcqDevice.SetFeatureValue("flatfieldCalibrationPRNU", true);
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
    int NowLine = 0;

    void xfer_XferNotify(object sender, SapXferNotifyEventArgs args)
    {
      Protect(() =>
      {

        if (NowLine + FrameHeight > BufHeight)
          NowLine = FrameHeight;
        else
          NowLine = NowLine + FrameHeight;
      });
    }


    public void GetBufAddress(out IntPtr[] Datas)
    {

      int size = FrameWidth * BufHeight;
      Datas = new IntPtr[] { BufAddress, BufAddress + size, BufAddress + size * 2 };


    }

    public void GetBufAddress(out IntPtr R, out IntPtr G, out IntPtr B)
    {


      int size = FrameWidth * BufHeight;
      R = BufAddress;
      G = BufAddress + size;
      B = BufAddress + size * 2;


    }


    public IntPtr GetBufAddress()
    {

      return BufAddress;
    }


    public int GetCurrentLine()
    {
      return NowLine;
    }
    public void GetCurrentFrame(out IntPtr R, out IntPtr G, out IntPtr B)
    {
      int size = FrameWidth * BufHeight;
      int loc = FrameWidth * Math.Max(0, NowLine - FrameHeight);

      R = BufAddress + loc;
      G = BufAddress + loc + size;
      B = BufAddress + loc + size * 2;


    }
    public void GetCurrentFrame(out IntPtr[] Datas)
    {
      int size = FrameWidth * BufHeight;
      int loc = FrameWidth * Math.Max(0, NowLine - FrameHeight);

      Datas = new IntPtr[] { BufAddress + loc,
                                   BufAddress + loc+ size,
                                   BufAddress + loc+ size * 2};

    }

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
