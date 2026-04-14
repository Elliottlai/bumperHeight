using Machine.Core.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Machine.Core.Interfaces
{
    public interface ICameraArgs : IComponent
    {
        GrabModuleType Type { get; }

        //"名稱": "MasterCCD",
        //"系統": "PiranhaXLColor16K",

        string CCD_Name { set; get; }
        string CCD_ID { set; get; }

        double Gain { set; get; }
        
        double ExposureTime { set; get; }

        double Rate { set; get; }

        int PixelBytes { get; }

        int FrameWidth { set; get; }
        
        int FrameHeight { set; get; }
        int BufWidth { set; get; }

        int BufHeight { set; get; }


        double PixelWidth { set; get; }
        
        double PixelHeight { set; get; }

        string ConfigFileName { set; get; }

        // int BufHeight { get;  set; }  

    /*
    /// <summary>
    /// 修正參數
    /// </summary>
    double EncoderError_Y { set; get; }

    /// <summary>
    /// 亮度校正白紙目標值
    /// </summary>
    int WhitePaperLevel { set; get; } // Maybe not Handle
    */

  }
}
