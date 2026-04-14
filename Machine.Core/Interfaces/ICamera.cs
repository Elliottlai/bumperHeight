using Machine.Core.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Machine.Core.Interfaces
{
    public interface ICamera : ICameraArgs
    {

        bool Init();

        void Start(int BufIndex = -1);

        void Stop();

        IntPtr[] GetBufAddress(int index=-1);

        IntPtr[] GetCurrentFrame();
        
        bool SetFeatureValue(GMExpandParamter Param, params object[] value);

 

        object GetFeatureValue(GMExpandParamter Param );

  

        object FunctionCall(GMExpandFunction func, params object[] value);
       

    }
}
