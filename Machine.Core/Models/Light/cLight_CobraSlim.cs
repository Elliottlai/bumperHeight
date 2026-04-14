using CobraSlimComm;
using Machine.Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Machine.Core.Enums;
using System.IO.Ports;

namespace Machine.Core
{
    public class cLight_CobraSlim : ILight
    {
        
        public bool IsEnable { get; set; }

        public string UID { get; set; }

        public string Name { get; set; }

        public LightType Type => LightType.CobraSlim;

        public int Channel { get; set; }

        public string Network_IPAddress { get; set; }

        public int Network_Port { get; set; }

        public int Network_Timeout { get; set; }

        public string PortName { get; set; }

        public int BaudRate { get; set; }

        public Parity Parity { get; set; }

        public int DataBits { get; set; }

        public StopBits StopBits { get; set; }

        public int ReadTimeout { get; set; }

        public int WriteTimeout { get; set; }
        public int MaxLevel { get; set; } = 255;
        private CobraSlimEthernet cobraSlim;
        private bool isInitialized;

        /*  private Protocol ptcl;
          private bool isInitialized;
          private ControlModes mode;*/


        public cLight_CobraSlim()
        {
            this.IsEnable = true;
            Initialize();
        }



        public void Initialize()
        {
            if (!this.IsEnable) 
                return;

            //Dns.GetHostAddresses(Dns.GetHostName());
            IPHostEntry ipHostEntry = Dns.GetHostByName(Dns.GetHostName());

            bool hostIpSetting = false;
            string checkIp = "10.0.0.11";
            foreach (IPAddress ip in ipHostEntry.AddressList)
                if (ip.ToString() == checkIp) hostIpSetting = true;

            if (!hostIpSetting) throw new InvalidOperationException("請先設定HostIp = 10.0.0.11, SubMask = 255.0.0.0, GeteWay = 10.0.0.1");



            this.cobraSlim = new CobraSlimEthernet(Network_IPAddress, Network_Port);

            // //// 查詢燈源的IP位址。
            // this.ThrowIfError(
            //                        this.cobraSlim.SendReceive("IPA" + "?"));
            //string s = this.cobraSlim.ResponseString;

            this.cobraSlim.SetReceiveTimeout(Network_Timeout);

            this.isInitialized = true;

        }
        private void ThrowIfError(bool value)
        {
            if (!value)
            {
                string errorMessage = this.cobraSlim.Err;
                throw new Exception("Has Not Received Command " + errorMessage);
            }
        }
        public bool SetLuminance(byte level, bool Wait = true)
        {
            if (!this.isInitialized) return false;
            if (!this.IsEnable) return false ;

            this.ThrowIfError(
                this.cobraSlim.SendReceive($"GLI={level}"));
            return true;
        }
        public byte GetLuminance()
        {
            if (!this.isInitialized || !this.IsEnable) return default(int);
            this.ThrowIfError(this.cobraSlim.SendReceive("GLI" + "?"));
            return byte.Parse(this.cobraSlim.ResponseString);
        }
        public bool PowerOn()
        {
            if (!this.isInitialized) return false;

            this.ThrowIfError(
                this.cobraSlim.SendReceive("GSS" + "=" + "1"));
            return true;
        }
        public bool PowerOff()
        {
            if (!this.isInitialized) return false;

            this.ThrowIfError(
                this.cobraSlim.SendReceive("GSS" + "=" + "0"));
            return true;
        }
        public void Dispose()
        {
            if (!this.isInitialized) return;
            if (!this.IsEnable) return;

            this.cobraSlim.CloseConnection();
            this.isInitialized = false;
        }


    }
}
