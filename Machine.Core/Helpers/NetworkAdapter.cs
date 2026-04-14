using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Text;
using System.Threading.Tasks;


namespace Machine.Core
{
    public   class NetworkAdapter
    {
        private static ManagementObjectSearcher searcher;
        private ManagementObject adapter;

        static NetworkAdapter()
        {
            searcher = new ManagementObjectSearcher(
                "root\\CIMV2",
                "SELECT * FROM Win32_NetworkAdapter");
        }

        private NetworkAdapter(ManagementObject adapter)
        {
            this.adapter = adapter;
        }

        public string Name
        {
            get { return (string)this.adapter["NetConnectionID"] ?? string.Empty; }
        }

        public int DeviceID
        {
            get { return int.Parse((string)this.adapter["DeviceID"]); }
        }

        public string DeviceName
        {
            get { return (string)this.adapter["Name"]; }
        }

        public string Description  
        {
            get { return (string)this.adapter["Description"]; }
        }

        public static IEnumerable<NetworkAdapter> FindAll()
        {
            return searcher.Get()
                .Cast<ManagementObject>()
                .Select(obj => new NetworkAdapter(obj));
        }

        public static IEnumerable<NetworkAdapter> Find(Predicate<NetworkAdapter> predicate)
        {
            return FindAll().Where(adp => predicate(adp));
        }

        public void Enable()
        {
            ManagementBaseObject outParams =
                    this.adapter.InvokeMethod("Enable", null, null);
        }

        public void Disable()
        {
            ManagementBaseObject outParams =
                    this.adapter.InvokeMethod("Disable", null, null);
        }
    }
}
