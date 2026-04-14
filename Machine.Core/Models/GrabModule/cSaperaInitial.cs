using Basler.Pylon;
using DALSA.SaperaLT.SapClassBasic;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace Machine.Core.Models.GrabModule
{
    static class cSaperaInitial
    {
        static public bool _isInitialized { get; set; } = false;

        static public Dictionary<string, int> CameraServerIndex = new Dictionary<string, int>();

        static public void Initialize()
        {
            if (_isInitialized)
                return;
            Console.WriteLine("\n\nCameras listed by Serial Number:\n");
            string serialNumberName = "";

            int serverCount = SapManager.GetServerCount();

            for (int serverIndex = 0; serverIndex < serverCount; serverIndex++)
            {
                if (SapManager.GetResourceCount(serverIndex, SapManager.ResourceType.AcqDevice) != 0)
                {
                    SapLocation location = new SapLocation(SapManager.GetServerName(serverIndex), 0);
                    var acqDevice = new SapAcqDevice(location);

                    // Create acquisition device object
                    bool status = acqDevice.Create();
                    if (status && acqDevice.FeatureCount > 0)
                    {
                        // Get Serial Number Feature Value
                        status = acqDevice.GetFeatureValue("DeviceID", out serialNumberName);
                        CameraServerIndex.Add(serialNumberName, serverIndex);
                    }

                    // Destroy acquisition device object
                    if (!acqDevice.Destroy())
                        throw new Exception("Failed to destroy acquisition device object.");
                }
            }
            _isInitialized = true;
        }

    }
}
