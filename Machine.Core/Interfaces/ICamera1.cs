using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Machine.Core.Interfaces
{
    public interface ICamera1 : ICameraArgs
    {



        bool Init();

        void Start(bool IsInternal = true);

        void Stop();
        void GetBufAddress(out IntPtr[] Datas);
        void GetBufAddress(out IntPtr R, out IntPtr G, out IntPtr B);

        void GetCurrentFrame(out IntPtr[] Datas);
        void GetCurrentFrame(out IntPtr R , out IntPtr G, out IntPtr B);
        /*
        bool SetValus<T>(int No,  T value);
        bool GetValus<T>(int No, T value);
        */
        /*
        void PRNUSet();
        void SavePRNUSet(int index=-1);
              
        

        int GetCurrentLine();*/
    }
}
