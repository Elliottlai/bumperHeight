using Machine.Core.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Machine.Core.Interfaces
{
    /// <summary>
    /// 相機取像器所需參數組介面
    /// </summary>
    public interface IGraberCamArgs
    {
        double Gain { set; get; }
        double ExposureTime { set; get; }
        int PixelBytes { get; }
    }
}
