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
    public class cGM_InitConfig_Arena
    {
        public string ConfigFileName;
        public int CameraIndex;      //相機連接id
        public cGM_InitConfig_Arena()
        {
            ConfigFileName = @"";
            CameraIndex = 0;
        }
        public cGM_InitConfig_Arena(string file, int id)
        {
            ConfigFileName = file;
            CameraIndex = id;
        }
    }
    public class cGM_InitConfig_Dasal
    {
        public string ConfigFileName;
        //SapAcquisition
        public int GraberServer;        //影像卡server id (Acq)
        public int GraberResource;      //影像卡資源連接埠id
        //SapAcqDevice
        public int CameraServer;        //相機server id (AcqDevice)
        public int CameraResource;      //相機連接埠id
        public cGM_InitConfig_Dasal()
        {
            ConfigFileName = @"C:\Windows\System32\Setting_Vtest\T_NanoCL-C4040_Default.ccf";
            GraberServer = 2;
            GraberResource = 4;
            CameraServer = 3;
            CameraResource = 0;
        }
        public cGM_InitConfig_Dasal(string file, int gs, int gr, int cs, int cr)
        {
            GraberServer = gs;
            GraberResource = gr;
            CameraServer = cs;
            CameraResource = cr;
        }
    }
}
