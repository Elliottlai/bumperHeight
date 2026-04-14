using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Machine.Core.Interfaces;

namespace FoupInspecMachine.Models
{
    public class OPT_Controller :  IDisposable
    {
        private string Port = "COM9";
        private string name = "OPT";
        private bool isDisposed = false;

        private int maxValue = 255;
        private int minValue = 0;

        public OPT_Controller(string portName, int baudRate = 115200)
        {
            controller = new OPTControllerAPI();
            Port = portName;
 
            
        }

        private OPTControllerAPI controller = null;

        public bool IsOpen => controller.IsConnect() == 0;

        public string Name => this.name;

        public void Close()
        {
            if (!this.IsOpen) return;
            long lRet = -1;
            lRet = controller.ReleaseSerialPort();
            if (0 != lRet)
            {
                throw new Exception("Failed to release serial port");
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (this.isDisposed) return;
            if (this.IsOpen) this.Close();
            this.isDisposed = true;
        }


        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        public int GetValue(int channel)
        {
            if (this.isDisposed) return 0;

            int value = 0;

            controller.ReadIntensity(channel, ref value);

            return ConvertDeviceValueToPercentage(value);
        }

        public void Open()
        {
            long lRet = -1;

            if (this.IsOpen) return;
            lRet = controller.InitSerialPort(this.Port);
            if (0 != lRet)
            {
                throw new Exception($"Failed to initialize serial port {this.Port}");
            }
        }

        public void SetValue(int channel, int value)
        {
            var a = controller.SetIntensity(channel, ConvertPercentageToDeviceValue(value));
        }

        public int SetValue(List<byte> channelValue)
        {
            if (!IsOpen)
            {
                throw new InvalidOperationException("Not openned.");
            }

            if (isDisposed)
                return 0;

            List<OPTControllerAPI.IntensityItem> channels = new List<OPTControllerAPI.IntensityItem>();

            for (int i = 0; i < channelValue.Count; i++)
            {
                channels.Add(new OPTControllerAPI.IntensityItem() { channel = i + 1, intensity = ConvertPercentageToDeviceValue(channelValue[i]) });
            }

            return controller.SetMultiIntensity(channels.ToArray(), channels.ToArray().Length);
        }

        public byte[] GetValues()
        {
            if (!IsOpen)
            {
                throw new InvalidOperationException("Not openned.");
            }



            List<byte> channels = new List<byte>();

            for (int i = 0; i < 4; i++)
            {
                int value = 0;
                controller.ReadIntensity(i + 1, ref value);

                channels.Add((byte)ConvertDeviceValueToPercentage(value));
            }

            return channels.ToArray();
        }

        private int ConvertPercentageToDeviceValue(int percent)
        {
            percent = Math.Max(0, Math.Min(100, percent)); // 限制在 0~100
            double ratio = percent / 100.0;
            int deviceValue = (int)Math.Round(ratio * 255);
            return deviceValue;
        }

        private int ConvertDeviceValueToPercentage(int deviceValue)
        {
            deviceValue = Math.Max(0, Math.Min(255, deviceValue)); // 限制在 0~255
            double ratio = deviceValue / 255.0;
            int percent = (int)Math.Round(ratio * 100);
            return percent;
        }
    }
}
