using Machine.Core.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Machine.Core.Interfaces
{
    /// <summary>
    /// 取像器所需基本參數組介面
    /// </summary>
    public interface IGraberBaseArgs : IComponent
    {
        GrabModuleType Type { get; }
        string CCD_Name { get; set; }
        string CCD_ID { get; set; }
        double Rate { get; set; }
        int FrameWidth { get; }
        int FrameHeight { get; }
        double PixelWidth { get; }
        double PixelHeight { get; }
        int BufWidth { get; }
        int BufHeight { get; }
        string ConfigFileName { get; set; }
    }
}
