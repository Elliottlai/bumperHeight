using Machine.Core.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Machine.Core.Interfaces
{
    /// <summary>
    /// 3D取像器所需參數組介面
    /// </summary>
    public interface IGraber3DArgs
    {
        string SsensorIP { get; set; }
        bool UseAccelerator { get; set; }  
        int GrabTimeout { get; set; }
    }
}
