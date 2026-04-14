using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Machine.Core.Interfaces
{
    public interface IGraber : IDisposable
    {
        bool Init();

        void Start();
        void Stop();

        void GetBufAddress(out IntPtr[] Datas);
        //IntPtr[] GetBufAddress();

        void GetCurrentFrame(out IntPtr[] Datas);
        //IntPtr[] TryGetCurrentFrame();

        //void SetFeatureValus<T>(string featureName, T value);
        //void GetFeatureValus<T>(string featureName,out T value);
        void SetFeatureValue(string featureName, object value);
        object GetFeatureValue(string featureName);
        void CallFunction(string functionName, List<object> args);
    }
}
