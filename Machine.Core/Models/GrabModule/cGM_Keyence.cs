using Machine.Core.Enums;
using Machine.Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Linq;
using System.Text;
using System.IO;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading.Tasks;
using System.Threading;

namespace Machine.Core
{
    public class cGM_Keyence : IGraber, IGraberBaseArgs, IGraber3DArgs
    {



        #region SendCommand Enum
        private enum SendCommand
        {
            /// <summary>None</summary>
            None,
            /// <summary>Restart</summary>
            RebootController,
            /// <summary>Trigger</summary>
            Trigger,
            /// <summary>Start measurement</summary>
            StartMeasure,
            /// <summary>Stop measurement</summary>
            StopMeasure,
            /// <summary>Get profiles</summary>
            GetProfile,
            /// <summary>Get batch profiles</summary>
            GetBatchProfile,
            /// <summary>Initialize Ethernet high-speed data communication</summary>
            InitializeHighSpeedDataCommunication,
            /// <summary>Request preparation before starting high-speed data communication</summary>
            PreStartHighSpeedDataCommunication,
            /// <summary>Start high-speed data communication</summary>
            StartHighSpeedDataCommunication,
        }
        #endregion
        /// <summary>Array of controller information</summary>
		private DeviceData[] _deviceData;
        /// <summary>Current device ID</summary>
		private int _currentDeviceId;
        /// <summary>Send command</summary>
        private SendCommand _sendCommand;
        /// <summary>Ethernet settings structure </summary>
		private LJX8IF_ETHERNET_CONFIG _ethernetConfig;
        /// <summary>Callback function used during high-speed communication (simple array)</summary>
		private HighSpeedDataCallBackForSimpleArray _callbackSimpleArray;
        /// <summary>
		/// High-speed communication start request structure
		/// </summary>
		private LJX8IF_HIGH_SPEED_PRE_START_REQUEST _request;
        /// <summary>Array of profile information structures</summary>
		private LJX8IF_PROFILE_INFO[] _profileInfo;
        /// <summary>Array of value of receive buffer is full</summary>
		private static bool[] _isBufferFull = new bool[NativeMethods.DeviceCount];
        /// <summary>Array of value of stop processing has done by buffer full error</summary>
		private static bool[] _isStopCommunicationByError = new bool[NativeMethods.DeviceCount];
        /// <summary>Array of labels that indicate the number of received profiles </summary>
        //private Label[] _receivedProfileCountLabels;







        private void ReceiveHighSpeedSimpleArray(IntPtr headBuffer, IntPtr profileBuffer, IntPtr luminanceBuffer, uint isLuminanceEnable, uint profileSize, uint count, uint notify, uint user)
        {
            // @Point
            // Take care to only implement storing profile data in a thread save buffer in the callback function.
            // As the thread used to call the callback function is the same as the thread used to receive data,
            // the processing time of the callback function affects the speed at which data is received,
            // and may stop communication from being performed properly in some environments.
            //high = profileBuffer;
            //lumi = luminanceBuffer;

            _isBufferFull[(int)user] = _deviceData[(int)user].SimpleArrayDataHighSpeed.AddReceivedData(profileBuffer, luminanceBuffer, count);
            _deviceData[(int)user].SimpleArrayDataHighSpeed.Notify = notify;
        }

        public GrabModuleType Type => GrabModuleType.Keyence;

        public string CCD_Name { get; set; } = "Keyence LJX8200";
        public string CCD_ID { get; set; }
        public double Rate { get; set; }

        public int FrameWidth { get; set; }

        public int FrameHeight { get; set; }

        public double PixelWidth { get; set; }

        public double PixelHeight { get; set; }

        public int BufWidth { get; set; }

        public int BufHeight { get; set; }

        public string ConfigFileName { get; set; }
        public string UID { get; set; }
        public string Name { get; set; }
        public string SsensorIP { get; set; } = "192.168.0.1";
        public bool UseAccelerator { get; set; } = false;
        public int GrabTimeout { get; set; } = 10000;
        public IntPtr high;
        public IntPtr lumi;


        public void CallFunction(string functionName, List<object> args)
        {
            throw new NotImplementedException();
        }

        public void Dispose()
        {
            throw new NotImplementedException();
        }



        public void GetCurrentFrame(out IntPtr[] Datas)
        {
            Start();
            Stop();
            GetBufAddress(out Datas);
        }

        public object GetFeatureValue(string featureName)
        {
            throw new NotImplementedException();
        }

        public bool Init()
        {
            Rc rc = (Rc)NativeMethods.LJX8IF_Initialize();
            _callbackSimpleArray = ReceiveHighSpeedSimpleArray;



            _deviceData = new DeviceData[NativeMethods.DeviceCount];
            for (int i = 0; i < NativeMethods.DeviceCount; i++)
            {
                _deviceData[i] = new DeviceData();
            }

            _deviceData[_currentDeviceId].Status = DeviceStatus.NoConnection;
            _profileInfo = new LJX8IF_PROFILE_INFO[NativeMethods.DeviceCount];
            ///initial Ethernet connection
            if (!CheckReturnCode(rc)) return false;

            // Open the communication path
            // Generate the settings for Ethernet communication.
            try
            {
                _ethernetConfig.abyIpAddress = Utility.GetIpAddressFromTextBox("192", "168", "0", "1");
                _ethernetConfig.wPortNo = Convert.ToUInt16("24691");
            }
            catch (Exception ex)
            {
                //MessageBox.Show(this, ex.Message);
                return false;
            }
            rc = (Rc)NativeMethods.LJX8IF_EthernetOpen(Define.DeviceId, ref _ethernetConfig);

            if (!CheckReturnCode(rc)) return false;
            return true;
        }

        public void SetFeatureValue(string featureName, object value)
        {
            throw new NotImplementedException();
        }

        public void Start()
        {
            //_sendCommand = SendCommand.InitializeHighSpeedDataCommunication;

            ThreadSafeBuffer.ClearBuffer(_currentDeviceId);  //Clear the retained profile data.
            _deviceData[_currentDeviceId].ProfileDataHighSpeed.Clear();
            _deviceData[_currentDeviceId].SimpleArrayDataHighSpeed.Clear();

            ushort HighSpeedPortNo = 24692;
            uint ProfileCount = 1;

            LJX8IF_ETHERNET_CONFIG ethernetConfig = _ethernetConfig;
            int rc = NativeMethods.LJX8IF_InitializeHighSpeedDataCommunicationSimpleArray(_currentDeviceId, ref ethernetConfig,
                HighSpeedPortNo, _callbackSimpleArray,
                ProfileCount, (uint)_currentDeviceId);

            //AddLogResult(rc, "INITIALIZE_HIGH_SPEED_DATA_ETHERNET_COMMUNICATION_SIMPLE_ARRAY");

            if (rc == (int)Rc.Ok)
            {
                _deviceData[_currentDeviceId].Status = DeviceStatus.EthernetFast;
                _deviceData[_currentDeviceId].EthernetConfig = ethernetConfig;
            }


            //接下來要開始PreStartHighSpeed
            //prStar _request 決定 起始位置
            _request.bySendPosition = 2;
            _request.reserve = null;
            LJX8IF_PROFILE_INFO profileInfo = new LJX8IF_PROFILE_INFO();
            rc = NativeMethods.LJX8IF_PreStartHighSpeedDataCommunication(_currentDeviceId, ref _request, ref profileInfo);
            //AddLogResult(rc, "IDS_PRE_START_HIGH_SPEED_DATA_COMMUNICATION");
            if (rc != (int)Rc.Ok) return;

            // Response data display
            //AddLog(Utility.ConvertProfileInfoToLogString(profileInfo).ToString());

            _deviceData[_currentDeviceId].SimpleArrayDataHighSpeed.Clear();
            _deviceData[_currentDeviceId].SimpleArrayDataHighSpeed.DataWidth = profileInfo.nProfileDataCount;
            _deviceData[_currentDeviceId].SimpleArrayDataHighSpeed.IsLuminanceEnable = profileInfo.byLuminanceOutput == 1;

            _profileInfo[_currentDeviceId] = profileInfo;

            //// 開始Start high speed data Communication
            ThreadSafeBuffer.ClearBuffer(_currentDeviceId);
            _deviceData[_currentDeviceId].ProfileDataHighSpeed.Clear();
            _isBufferFull[_currentDeviceId] = false;
            _isStopCommunicationByError[_currentDeviceId] = false;
            rc = NativeMethods.LJX8IF_StartHighSpeedDataCommunication(_currentDeviceId);

            //AddLogResult(rc, "IDS_START_HIGH_SPEED_DATA_COMMUNICATION");


            //_timerHighSpeedReceive.Interval = 500;
            //_timerHighSpeedReceive.Start();


            // start
            rc = NativeMethods.LJX8IF_StartMeasure(_currentDeviceId);
            //AddLogResult(rc, "IDS_START_MEASURE");

        }


        ushort[] hData = null;
        public void Stop()
        {
            //stop
            int rc = NativeMethods.LJX8IF_StopMeasure(_currentDeviceId);
            //AddLogResult(rc, "IDS_STOP_MEASURE");


            //Save 數據
            int width = _deviceData[_currentDeviceId].SimpleArrayDataHighSpeed.DataWidth;
            if (width == 0)
            {
                //AddLog("No simple array data.");
                return;
            }

            string path = @"D:\\Joe\\CSV\\234.csv";

            if (string.IsNullOrEmpty(path))
            {
                //AddLog("Failed to save image. (File path is empty.)");
                return;
            }

            //Cursor.Current = Cursors.WaitCursor;

            int startIndex = (int)0;
            int dataCount = (int)2500;

            hData = _deviceData[_currentDeviceId].SimpleArrayDataHighSpeed.ProfileSimpleArrayData.ToArray();
            ushort k = hData.Max();
            ushort p = _deviceData[_currentDeviceId].SimpleArrayDataHighSpeed.ProfileSimpleArrayData.Max();
            bool result = _deviceData[_currentDeviceId].SimpleArrayDataHighSpeed.SaveDataAsImages(path, startIndex, dataCount);

            //AddLog(result ? "Succeed to save image." : "Failed to save image.");
            // 結束 Stop high speed data communication
            rc = NativeMethods.LJX8IF_StopHighSpeedDataCommunication(_currentDeviceId);
            //AddLogResult(rc, "IDS_STOP_HIGH_SPEED_DATA_COMMUNICATION");

            // 結束 Finalize high speed data communication
            rc = NativeMethods.LJX8IF_FinalizeHighSpeedDataCommunication(_currentDeviceId);
            //AddLogResult(rc, "IDS_FINALIZE_HIGH_SPEED_DATA_COMMUNICATION");
            switch (_deviceData[_currentDeviceId].Status)
            {
                case DeviceStatus.EthernetFast:
                    LJX8IF_ETHERNET_CONFIG config = _deviceData[_currentDeviceId].EthernetConfig;
                    _deviceData[_currentDeviceId].Status = DeviceStatus.Ethernet;
                    _deviceData[_currentDeviceId].EthernetConfig = config;
                    break;
            }


        }

        private bool CheckReturnCode(Rc rc)
        {
            if (rc == Rc.Ok) return true;
            //MessageBox.Show(this, string.Format("Error: 0x{0,8:x}", rc));
            return false;
        }

        public unsafe void GetBufAddress(out IntPtr[] Datas)
        {
            ushort k = hData.Max();
            short[] shData = new short[hData.Length];
            for (int i = 0; i != hData.Length; i++)
            {
                shData[i] = (short)((hData[i] - 32768));
            }


            int a = shData.Max();
            ushort[] lData = _deviceData[_currentDeviceId].SimpleArrayDataHighSpeed.LuminanceSimpleArrayData.ToArray();
            short[] slData = new short[lData.Length];
            unsafe
            {
                IntPtr ptr0, ptr1;
                fixed (short* p0 = shData)
                {
                    ptr0 = (IntPtr)p0;
                }
                fixed (ushort* p1 = lData)
                {
                    ptr1 = (IntPtr)p1;
                }
                Datas = new IntPtr[] { ptr0, ptr1 };
            }
        }

        private short[] ConvertHighData(ushort[] hData)
        {
            short[] temp = new short[hData.Count()];

            for (int i = 0; i != hData.Count(); i++)
            {
                temp[i] = (short)((hData[i] - 32768) * 4);
            }


            return temp;

        }


    }



    #region Enum
    /// <summary>Device communication state</summary>
    public enum DeviceStatus
    {
        NoConnection = 0,
        Ethernet,
        EthernetFast,
    };
    #endregion

    #region DeviceData
    /// <summary>
    /// Device data class
    /// </summary>
    public class DeviceData
    {
        #region Field
        /// <summary>Connection state</summary>
        private DeviceStatus _status = DeviceStatus.NoConnection;

        #endregion

        #region Property
        /// <summary>
        /// Status property
        /// </summary>
        public DeviceStatus Status
        {
            get { return _status; }
            set
            {
                ProfileData.Clear();
                ProfileDataHighSpeed.Clear();
                SimpleArrayData.Clear();
                SimpleArrayDataHighSpeed.Clear();
                EthernetConfig = new LJX8IF_ETHERNET_CONFIG();
                _status = value;
            }
        }

        /// <summary>Ethernet settings</summary>
        public LJX8IF_ETHERNET_CONFIG EthernetConfig { get; set; }
        /// <summary>Profile data</summary>
        public List<ProfileData> ProfileData { get; }
        /// <summary>Profile data for high speed communication</summary>
        public List<ProfileData> ProfileDataHighSpeed { get; }
        /// <summary>Simple array data</summary>
        public ProfileSimpleArrayStore SimpleArrayData { get; }
        /// <summary>Simple array data for high speed communication</summary>
        public ProfileSimpleArrayStore SimpleArrayDataHighSpeed { get; }
        #endregion

        #region Constructor
        /// <summary>
        /// Constructor
        /// </summary>
        public DeviceData()
        {
            EthernetConfig = new LJX8IF_ETHERNET_CONFIG();
            ProfileData = new List<ProfileData>();
            ProfileDataHighSpeed = new List<ProfileData>();
            SimpleArrayData = new ProfileSimpleArrayStore(); ;
            SimpleArrayDataHighSpeed = new ProfileSimpleArrayStore(); ;
        }
        #endregion

        #region Method
        /// <summary>
        /// Connection status acquisition
        /// </summary>
        /// <returns>Connection status for display</returns>
        public string GetStatusString()
        {
            string status = _status.ToString();
            switch (_status)
            {
                case DeviceStatus.Ethernet:
                case DeviceStatus.EthernetFast:
                    status += string.Format("---{0}.{1}.{2}.{3}", EthernetConfig.abyIpAddress[0], EthernetConfig.abyIpAddress[1],
                        EthernetConfig.abyIpAddress[2], EthernetConfig.abyIpAddress[3]);
                    break;
            }
            return status;
        }
        #endregion
    }
    #endregion

    #region ProfileData
    /// <summary>
    /// Profile data class
    /// </summary>
    public class ProfileData
    {
        #region constant
        private const int LUMINANCE_OUTPUT_ON_VALUE = 1;
        public const int MULTIPLE_VALUE_FOR_LUMINANCE_OUTPUT = 2;
        #endregion

        #region Field
        /// <summary>
        /// Profile data
        /// </summary>
        private int[] _profData;

        /// <summary>
        /// Profile information
        /// </summary>
        private LJX8IF_PROFILE_INFO _profileInfo;

        #endregion

        #region Property
        /// <summary>
        /// Profile Data
        /// </summary>
        public int[] ProfData
        {
            get { return _profData; }
        }

        /// <summary>
        /// Profile Imformation
        /// </summary>
        public LJX8IF_PROFILE_INFO ProfInfo
        {
            get { return _profileInfo; }
        }
        #endregion

        #region Method
        /// <summary>
        /// Constructor
        /// </summary>
        public ProfileData(int[] receiveBuffer, LJX8IF_PROFILE_INFO profileInfo)
        {
            SetData(receiveBuffer, profileInfo);
        }

        /// <summary>
        /// Constructor Overload
        /// </summary>
        /// <param name="receiveBuffer">Receive buffer</param>
        /// <param name="startIndex">Start position</param>
        /// <param name="profileInfo">Profile information</param>
        public ProfileData(int[] receiveBuffer, int startIndex, LJX8IF_PROFILE_INFO profileInfo)
        {
            int bufIntSize = CalculateDataSize(profileInfo);
            int[] bufIntArray = new int[bufIntSize];
            _profileInfo = profileInfo;

            Array.Copy(receiveBuffer, startIndex, bufIntArray, 0, bufIntSize);
            SetData(bufIntArray, profileInfo);
        }

        /// <summary>
        /// Set the members to the arguments.
        /// </summary>
        /// <param name="receiveBuffer">Receive buffer</param>
        /// <param name="profileInfo">Profile information</param>
        private void SetData(int[] receiveBuffer, LJX8IF_PROFILE_INFO profileInfo)
        {
            _profileInfo = profileInfo;

            // Extract the header.
            int headerSize = Utility.GetByteSize(Utility.TypeOfStructure.ProfileHeader) / Marshal.SizeOf(typeof(int));
            int[] headerData = new int[headerSize];
            Array.Copy(receiveBuffer, 0, headerData, 0, headerSize);

            // Extract the footer.
            int footerSize = Utility.GetByteSize(Utility.TypeOfStructure.ProfileFooter) / Marshal.SizeOf(typeof(int));
            int[] footerData = new int[footerSize];
            Array.Copy(receiveBuffer, receiveBuffer.Length - footerSize, footerData, 0, footerSize);

            // Extract the profile data.
            int profSize = receiveBuffer.Length - headerSize - footerSize;
            _profData = new int[profSize];
            Array.Copy(receiveBuffer, headerSize, _profData, 0, profSize);
        }

        /// <summary>
        /// Data size calculation
        /// </summary>
        /// <param name="profileInfo">Profile information</param>
        /// <returns>Profile data size</returns>
        public static int CalculateDataSize(LJX8IF_PROFILE_INFO profileInfo)
        {
            LJX8IF_PROFILE_HEADER header = new LJX8IF_PROFILE_HEADER();
            LJX8IF_PROFILE_FOOTER footer = new LJX8IF_PROFILE_FOOTER();

            int multipleValue = GetIsLuminanceOutput(profileInfo) ? MULTIPLE_VALUE_FOR_LUMINANCE_OUTPUT : 1;
            return profileInfo.nProfileDataCount * multipleValue + (Marshal.SizeOf(header) + Marshal.SizeOf(footer)) / Marshal.SizeOf(typeof(int));
        }

        public static bool GetIsLuminanceOutput(LJX8IF_PROFILE_INFO profileInfo)
        {
            return profileInfo.byLuminanceOutput == LUMINANCE_OUTPUT_ON_VALUE;
        }

        /// <summary>
        /// Create the X-position string from the profile information.
        /// </summary>
        /// <param name="profileInfo">Profile information</param>
        /// <returns>X-position string</returns>
        public static string GetXPositionString(LJX8IF_PROFILE_INFO profileInfo)
        {
            StringBuilder sb = new StringBuilder();
            // Data position calculation
            double posX = profileInfo.lXStart;
            double deltaX = profileInfo.lXPitch;

            int singleProfileCount = profileInfo.nProfileDataCount;
            int dataCount = profileInfo.byProfileCount;

            for (int i = 0; i < dataCount; i++)
            {
                for (int j = 0; j < singleProfileCount; j++)
                {
                    sb.AppendFormat("{0}\t", (posX + deltaX * j));
                }
            }
            return sb.ToString();
        }

        #endregion
    }

    #endregion

    #region ProfileSimpleArrayStore
    /// <summary>
	/// Store simple array data both profile and luminance.
	/// </summary>
	public class ProfileSimpleArrayStore
    {
        [DllImport("kernel32.dll", SetLastError = false)]
        private static extern void CopyMemory(IntPtr destination, IntPtr source, UIntPtr length);

        #region constant
        private const int BatchFinalizeFlagBitCount = 16;
        #endregion

        #region Field
        /// <summary>Object for exclusive control</summary>
        private readonly object _syncObject = new object();

        private uint _count;
        private uint _notify;

        /// <summary>Profile data (simple array)</summary>
        private readonly List<ushort> _profileData = new List<ushort>();
        /// <summary>Luminance data (simple array)</summary>
        private readonly List<ushort> _luminanceData = new List<ushort>();
        #endregion

        #region Property
        /// <summary>Profile data size</summary>
        public int DataWidth { get; set; }
        /// <summary>The value indicating whether luminance data output is enable or not</summary>
        public bool IsLuminanceEnable { private get; set; }
        /// <summary>Latest batch number</summary>
        public int BatchNo { get; private set; }

        /// <summary>Stored profile count</summary>
        public uint Count
        {
            get
            {
                lock (_syncObject)
                {
                    return _count;
                }
            }
            set
            {
                lock (_syncObject)
                {
                    _count = value;
                }
            }
        }

        /// <summary>Callback function notification parameter (for high-speed communication)</summary>
        public uint Notify
        {
            get
            {
                lock (_syncObject)
                {
                    uint value = _notify;
                    _notify = 0;
                    return value;
                }
            }
            set
            {
                lock (_syncObject)
                {
                    if ((uint)(value & (0x1 << BatchFinalizeFlagBitCount)) != 0)
                    {
                        BatchNo++;
                    }

                    _notify |= value;
                }
            }
        }
        #endregion

        #region Method
        /// <summary>
        /// Add received data from device.
        /// </summary>
        /// <param name="profileBuffer">A pointer to the buffer that stores the profile data array</param>
        /// <param name="luminanceBuffer">A pointer to the buffer that stores the luminance profile data array</param>
        /// <param name="count">The number of profile data stored in buffer</param>
        /// <returns>True if buffer full</returns>
        public bool AddReceivedData(IntPtr profileBuffer, IntPtr luminanceBuffer, uint count)
        {
            lock (_syncObject)
            {
                if (DataWidth <= 0)
                {
                    return Count >= Define.BufferFullCount;
                }

                uint copyCount = Math.Min((uint)Define.BufferFullCount - Count, count);
                if (copyCount == 0) return Count >= Define.BufferFullCount;

                Count += copyCount;
                int bufferSize = DataWidth * (int)copyCount;
                ushort[] buffer = new ushort[bufferSize];
                CopyUshort(profileBuffer, buffer, bufferSize);
                _profileData.AddRange(buffer);

                if (IsLuminanceEnable)
                {
                    CopyUshort(luminanceBuffer, buffer, bufferSize);
                    _luminanceData.AddRange(buffer);
                }

                return Count >= Define.BufferFullCount;
            }
        }

        public List<ushort> ProfileSimpleArrayData { get => _profileData; }
        public List<ushort> LuminanceSimpleArrayData { get => _luminanceData; }

        /// <summary>
        /// Clear all data and property to default.
        /// </summary>
        public void Clear()
        {
            lock (_syncObject)
            {
                BatchNo = 0;
                Count = 0;
                _notify = 0;
                DataWidth = 0;
                IsLuminanceEnable = false;
                _profileData.Clear();
                _luminanceData.Clear();
            }
        }

        /// <summary>
        /// Save current profile data and luminance data to image file.
        /// </summary>
        /// <param name="filePath">The file path to save</param>
        /// <param name="index">Index of the profile to save</param>
        /// <param name="count">Number of profile to save</param>
        /// <returns>True if save succeed</returns>
        public bool SaveDataAsImages(string filePath, int index, int count)
        {
            lock (_syncObject)
            {
                if (string.IsNullOrEmpty(filePath)) return false;

                if (DataWidth <= 0 || _profileData == null)
                {
                    return false;
                }

                int profileHeight = _profileData.Count() / DataWidth;
                if (count <= 0 || index > profileHeight || index + count > profileHeight)
                {
                    return false;
                }

                string pathBase = Path.Combine(Path.GetDirectoryName(filePath), Path.GetFileNameWithoutExtension(filePath));

                SaveBitmap(index, count, pathBase);
                SaveTiff(index, count, pathBase);

                return true;
            }
        }

        private void SaveBitmap(int index, int count, string pathBase)
        {
            const string heightSuffix = "_height.bmp";
            const string luminanceSuffix = "_luminance.bmp";

            lock (_syncObject)
            {
                SaveBitmapCore(pathBase + heightSuffix, _profileData, index, count);

                if (IsLuminanceEnable)
                {
                    SaveBitmapCore(pathBase + luminanceSuffix, _luminanceData, index, count);
                }
            }
        }

        private void SaveTiff(int index, int count, string pathBase)
        {
            const string heightSuffix = "_height.tif";
            const string luminanceSuffix = "_luminance.tif";

            lock (_syncObject)
            {
                SaveTiffCore(pathBase + heightSuffix, _profileData, index, count);

                if (IsLuminanceEnable)
                {
                    SaveTiffCore(pathBase + luminanceSuffix, _luminanceData, index, count);
                }
            }
        }

        /// <summary>
        /// Save bitmap565 image.
        /// </summary>
        /// <param name="filePath">The file path to save</param>
        /// <param name="data">The bitmap pixel data</param>
        /// <param name="index">Index of the profile to save</param>
        /// <param name="count">Number of profile to save</param>
        private void SaveBitmapCore(string filePath, IEnumerable<ushort> data, int index, int count)
        {
            ushort[] profile = data.Skip(index * DataWidth).Take(count * DataWidth).ToArray();
            int profileHeight = profile.Count() / DataWidth;
            using (PinnedObject pin = new PinnedObject(profile))
            using (Bitmap bmp = new System.Drawing.Bitmap(DataWidth, profileHeight, DataWidth * 2, PixelFormat.Format16bppRgb565, pin.Pointer))
            {
                bmp.Save(filePath, ImageFormat.Bmp);
            }
        }

        /// <summary>
        /// Save TIFF image.
        /// </summary>
        /// <param name="filePath">The file path to save</param>
        /// <param name="data">The bitmap pixel data</param>
        /// <param name="index">Index of the profile to save</param>
        /// <param name="count">Number of profile to save</param>
        private void SaveTiffCore(string filePath, IEnumerable<ushort> data, int index, int count)
        {
            byte[] profile = data.Skip(index * DataWidth).Take(count * DataWidth).SelectMany(BitConverter.GetBytes).ToArray();

            int profileHeight = profile.Count() / 2 / DataWidth;
            using (FileStream stream = new FileStream(filePath, FileMode.Create))
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                WriteTiffHeader(stream, (uint)DataWidth, (uint)profileHeight);

                writer.Write(profile);
            }
        }

        private static void WriteTiffHeader(Stream stream, uint width, uint height)
        {
            // <header(8)> + <tag count(2)> + <tag(12)>*12 + <next IFD(4)> + <resolution unit(8)>*2
            const uint stripOffset = 174;

            stream.Position = 0;
            // Header (little endian)
            stream.Write(new byte[] { 0x49, 0x49, 0x2A, 0x00, 0x08, 0x00, 0x00, 0x00 }, 0, 8);

            // Tag count
            stream.Write(new byte[] { 0x0C, 0x00 }, 0, 2);

            // Image Width
            WriteTiffTag(stream, 0x0100, 3, 1, width);

            // Image Length
            WriteTiffTag(stream, 0x0101, 3, 1, height);

            // Bits per sample
            WriteTiffTag(stream, 0x0102, 3, 1, 16);

            // Compression (no compression)
            WriteTiffTag(stream, 0x0103, 3, 1, 1);

            // Photometric interpretation (white mode & monochrome)
            WriteTiffTag(stream, 0x0106, 3, 1, 1);

            // Strip offsets
            WriteTiffTag(stream, 0x0111, 3, 1, stripOffset);

            // Rows per strip
            WriteTiffTag(stream, 0x0116, 3, 1, height);

            // strip byte counts
            WriteTiffTag(stream, 0x0117, 4, 1, width * height * (uint)2);

            // X resolusion address
            WriteTiffTag(stream, 0x011A, 5, 1, stripOffset - 16);

            // Y resolusion address
            WriteTiffTag(stream, 0x011B, 5, 1, stripOffset - 8);

            // Resolusion unit (inch)
            WriteTiffTag(stream, 0x0128, 3, 1, 2);

            // Color map (not use color map)
            WriteTiffTag(stream, 0x0140, 3, 1, 0);

            // Next IFD
            stream.Write(BitConverter.GetBytes((int)0), 0, 4);

            // X resolusion and Y resolusion
            stream.Write(BitConverter.GetBytes((int)96), 0, 4);
            stream.Write(BitConverter.GetBytes((int)1), 0, 4);
            stream.Write(BitConverter.GetBytes((int)96), 0, 4);
            stream.Write(BitConverter.GetBytes((int)1), 0, 4);
        }

        private static void WriteTiffTag(Stream stream, ushort kind, ushort dataType, uint dataSize, uint data)
        {
            List<byte> list = new List<byte>();
            list.AddRange(BitConverter.GetBytes(kind));
            list.AddRange(BitConverter.GetBytes(dataType));
            list.AddRange(BitConverter.GetBytes(dataSize));
            list.AddRange(BitConverter.GetBytes(data));
            byte[] tag = list.ToArray();
            stream.Write(tag, 0, tag.Length);
        }

        private static void CopyUshort(IntPtr source, ushort[] destination, int length)
        {
            // @Point
            // Copy array using kernel32's method manually because System.Runtime.InteropServices.Marshal.Copy method 
            // does not support ushort array.
            using (PinnedObject pin = new PinnedObject(destination))
            {
                int copyLength = Marshal.SizeOf(typeof(ushort)) * length;
                CopyMemory(pin.Pointer, source, (UIntPtr)copyLength);
            }
        }
        #endregion
    }
    #endregion

    #region NativeMethod
    #region Enum

    /// <summary>
    /// Return value definition
    /// </summary>
    public enum Rc
    {
        /// <summary>Normal termination</summary>
        Ok = 0x0000,
        /// <summary>Failed to open the device</summary>
        ErrOpenDevice = 0x1000,
        /// <summary>Device not open</summary>
        ErrNoDevice,
        /// <summary>Command send error</summary>
        ErrSend,
        /// <summary>Response reception error</summary>
        ErrReceive,
        /// <summary>Timeout</summary>
        ErrTimeout,
        /// <summary>No free space</summary>
        ErrNomemory,
        /// <summary>Parameter error</summary>
        ErrParameter,
        /// <summary>Received header format error</summary>
        ErrRecvFmt,

        /// <summary>Not open error (for high-speed communication)</summary>
        ErrHispeedNoDevice = 0x1009,
        /// <summary>Already open error (for high-speed communication)</summary>
        ErrHispeedOpenYet,
        /// <summary>Already performing high-speed communication error (for high-speed communication)</summary>
        ErrHispeedRecvYet,
        /// <summary>Insufficient buffer size</summary>
        ErrBufferShort,
    }

    /// Definition that indicates the "setting type" in LJX8IF_TARGET_SETTING structure.
    public enum SettingType : byte
    {
        /// <summary>Environment setting</summary>
        Environment = 0x01,
        /// <summary>Common measurement setting</summary>
        Common = 0x02,
        /// <summary>Measurement Program setting</summary>
        Program00 = 0x10,
        Program01,
        Program02,
        Program03,
        Program04,
        Program05,
        Program06,
        Program07,
        Program08,
        Program09,
        Program10,
        Program11,
        Program12,
        Program13,
        Program14,
        Program15,
    };


    /// Get batch profile position specification method designation
    public enum LJX8IF_BATCH_POSITION : byte
    {
        /// <summary>From current</summary>
        LJX8IF_BATCH_POSITION_CURRENT = 0x00,
        /// <summary>Specify position</summary>
        LJX8IF_BATCH_POSITION_SPEC = 0x02,
        /// <summary>From current after commitment</summary>
        LJX8IF_BATCH_POSITION_COMMITED = 0x03,
        /// <summary>Current only</summary>
        LJX8IF_BATCH_POSITION_CURRENT_ONLY = 0x04,
    };

    /// Setting value storage level designation
    public enum LJX8IF_SETTING_DEPTH : byte
    {
        /// <summary>Settings write area</summary>
        LJX8IF_SETTING_DEPTH_WRITE = 0x00,
        /// <summary>Active measurement area</summary>
        LJX8IF_SETTING_DEPTH_RUNNING = 0x01,
        /// <summary>Save area</summary>
        LJX8IF_SETTING_DEPTH_SAVE = 0x02,
    };


    /// Get profile target buffer designation
    public enum LJX8IF_PROFILE_BANK : byte
    {
        /// <summary>Active surface</summary>
        LJX8IF_PROFILE_BANK_ACTIVE = 0x00,
        /// <summary>Inactive surface</summary>	
        LJX8IF_PROFILE_BANK_INACTIVE = 0x01,
    };

    /// Get profile position specification method designation
    public enum LJX8IF_PROFILE_POSITION : byte
    {
        /// <summary>From current</summary>
        LJX8IF_PROFILE_POSITION_CURRENT = 0x00,
        /// <summary>From oldest</summary>
        LJX8IF_PROFILE_POSITION_OLDEST = 0x01,
        /// <summary>Specify position</summary>
        LJX8IF_PROFILE_POSITION_SPEC = 0x02,
    };

    #endregion

    #region Structure
    /// <summary>
    /// Version Information
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct LJX8IF_VERSION_INFO
    {
        public int nMajorNumber;
        public int nMinorNumber;
        public int nRevisionNumber;
        public int nBuildNumber;
    };

    /// <summary>
    /// Ethernet settings structure
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct LJX8IF_ETHERNET_CONFIG
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public byte[] abyIpAddress;
        public ushort wPortNo;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public byte[] reserve;

    };

    /// <summary>
    /// Setting item designation structure
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct LJX8IF_TARGET_SETTING
    {
        public byte byType;
        public byte byCategory;
        public byte byItem;
        public byte reserve;
        public byte byTarget1;
        public byte byTarget2;
        public byte byTarget3;
        public byte byTarget4;
    };

    /// <summary>
    /// Profile information structure
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct LJX8IF_PROFILE_INFO
    {
        public byte byProfileCount;
        public byte reserve1;
        public byte byLuminanceOutput;
        public byte reserve2;
        public short nProfileDataCount;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public byte[] reserve3;
        public int lXStart;
        public int lXPitch;
    };

    /// <summary>
    /// Profile header information structure
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct LJX8IF_PROFILE_HEADER
    {
        public uint reserve;
        public uint dwTriggerCount;
        public int lEncoderCount;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public uint[] reserve2;
    };

    /// <summary>
    /// Profile footer information structure
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct LJX8IF_PROFILE_FOOTER
    {
        public uint reserve;
    };

    /// <summary>
    /// Get profile request structure (batch measurement: off)
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct LJX8IF_GET_PROFILE_REQUEST
    {
        public byte byTargetBank;
        public byte byPositionMode;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public byte[] reserve;
        public uint dwGetProfileNo;
        public byte byGetProfileCount;
        public byte byErase;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public byte[] reserve2;
    };

    /// <summary>
    /// Get profile request structure (batch measurement: on)
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct LJX8IF_GET_BATCH_PROFILE_REQUEST
    {
        public byte byTargetBank;
        public byte byPositionMode;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public byte[] reserve;
        public uint dwGetBatchNo;
        public uint dwGetProfileNo;
        public byte byGetProfileCount;
        public byte byErase;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public byte[] reserve2;
    };

    /// <summary>
    /// Get profile response structure (batch measurement: off)
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct LJX8IF_GET_PROFILE_RESPONSE
    {
        public uint dwCurrentProfileNo;
        public uint dwOldestProfileNo;
        public uint dwGetTopProfileNo;
        public byte byGetProfileCount;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public byte[] reserve;
    };

    /// <summary>
    /// Get profile response structure (batch measurement: on)
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct LJX8IF_GET_BATCH_PROFILE_RESPONSE
    {
        public uint dwCurrentBatchNo;
        public uint dwCurrentBatchProfileCount;
        public uint dwOldestBatchNo;
        public uint dwOldestBatchProfileCount;
        public uint dwGetBatchNo;
        public uint dwGetBatchProfileCount;
        public uint dwGetBatchTopProfileNo;
        public byte byGetProfileCount;
        public byte byCurrentBatchCommited;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public byte[] reserve;
    };

    /// <summary>
    /// High-speed communication start preparation request structure
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct LJX8IF_HIGH_SPEED_PRE_START_REQUEST
    {
        public byte bySendPosition;     // Send start position
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public byte[] reserve;      // Reservation 
    };

    #endregion

    #region Method
    /// <summary>
    /// Callback function for high-speed communication
    /// </summary>
    /// <param name="pBuffer">Received profile data pointer</param>
    /// <param name="dwSize">Size in units of bytes of one profile</param>
    /// <param name="dwCount">Number of profiles</param>
    /// <param name="dwNotify">Finalization condition</param>
    /// <param name="dwUser">Thread ID</param>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void HighSpeedDataCallBack(IntPtr pBuffer, uint dwSize, uint dwCount, uint dwNotify, uint dwUser);

    /// <summary>
    /// Callback function for high-speed communication simple array
    /// </summary>
    /// <param name="pProfileHeaderArray">Received header data array pointer</param>
    /// <param name="pHeightProfileArray">Received profile data array pointer</param>
    /// <param name="pLuminanceProfileArray">Received luminance profile data array pointer</param>
    /// <param name="dwLuminanceEnable">The value indicating whether luminance data output is enable or not</param>
    /// <param name="dwProfileDataCount">The data count of one profile</param>
    /// <param name="dwCount">Number of profiles</param>
    /// <param name="dwNotify">Finalization condition</param>
    /// <param name="dwUser">Thread ID</param>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void HighSpeedDataCallBackForSimpleArray(IntPtr pProfileHeaderArray, IntPtr pHeightProfileArray, IntPtr pLuminanceProfileArray, uint dwLuminanceEnable, uint dwProfileDataCount, uint dwCount, uint dwNotify, uint dwUser);

    /// <summary>
    /// Function definitions
    /// </summary>
    internal class NativeMethods
    {
        /// <summary>
        /// Number of connectable devices
        /// </summary>
        internal static int DeviceCount
        {
            get { return 6; }
        }

        /// <summary>
        /// Fixed value for the bytes of environment settings data 
        /// </summary>
        internal static UInt32 EnvironmentSettingSize
        {
            get { return 60; }
        }

        /// <summary>
        /// Fixed value for the bytes of common measurement settings data 
        /// </summary>
        internal static UInt32 CommonSettingSize
        {
            get { return 20; }
        }

        /// <summary>
        /// Fixed value for the bytes of program settings data 
        /// </summary>
        internal static UInt32 ProgramSettingSize
        {
            get { return 10980; }
        }

        [DllImport("LJX8_IF.dll")]
        internal static extern int LJX8IF_Initialize();

        [DllImport("LJX8_IF.dll")]
        internal static extern int LJX8IF_Finalize();

        [DllImport("LJX8_IF.dll")]
        internal static extern LJX8IF_VERSION_INFO LJX8IF_GetVersion();

        [DllImport("LJX8_IF.dll")]
        internal static extern int LJX8IF_EthernetOpen(int lDeviceId, ref LJX8IF_ETHERNET_CONFIG pEthernetConfig);

        [DllImport("LJX8_IF.dll")]
        internal static extern int LJX8IF_CommunicationClose(int lDeviceId);

        [DllImport("LJX8_IF.dll")]
        internal static extern int LJX8IF_RebootController(int lDeviceId);

        [DllImport("LJX8_IF.dll")]
        internal static extern int LJX8IF_ReturnToFactorySetting(int lDeviceId);

        [DllImport("LJX8_IF.dll")]
        internal static extern int LJX8IF_ControlLaser(int lDeviceId, byte byState);

        [DllImport("LJX8_IF.dll")]
        internal static extern int LJX8IF_GetError(int lDeviceId, byte byReceivedMax, ref byte pbyErrCount, IntPtr pwErrCode);

        [DllImport("LJX8_IF.dll")]
        internal static extern int LJX8IF_ClearError(int lDeviceId, short wErrCode);

        [DllImport("LJX8_IF.dll")]
        internal static extern int LJX8IF_TrgErrorReset(int lDeviceId);

        [DllImport("LJX8_IF.dll")]
        internal static extern int LJX8IF_GetTriggerAndPulseCount(int lDeviceId, ref uint pdwTriggerCount, ref int plEncoderCount);

        [DllImport("LJX8_IF.dll")]
        internal static extern int LJX8IF_GetHeadTemperature(int lDeviceId, ref short pnSensorTemperature, ref short pnProcessorTemperature, ref short pnCaseTemperature);

        [DllImport("LJX8_IF.dll")]
        internal static extern int LJX8IF_GetSerialNumber(int lDeviceId, IntPtr pControllerSerialNo, IntPtr pHeadSerialNo);

        [DllImport("LJX8_IF.dll")]
        internal static extern int LJX8IF_GetAttentionStatus(int lDeviceId, ref ushort pwAttentionStatus);

        [DllImport("LJX8_IF.dll")]
        internal static extern int LJX8IF_Trigger(int lDeviceId);

        [DllImport("LJX8_IF.dll")]
        internal static extern int LJX8IF_StartMeasure(int lDeviceId);

        [DllImport("LJX8_IF.dll")]
        internal static extern int LJX8IF_StopMeasure(int lDeviceId);

        [DllImport("LJX8_IF.dll")]
        internal static extern int LJX8IF_ClearMemory(int lDeviceId);

        [DllImport("LJX8_IF.dll")]
        internal static extern int LJX8IF_SetSetting(int lDeviceId, byte byDepth, LJX8IF_TARGET_SETTING targetSetting, IntPtr pData, uint dwDataSize, ref uint pdwError);

        [DllImport("LJX8_IF.dll")]
        internal static extern int LJX8IF_GetSetting(int lDeviceId, byte byDepth, LJX8IF_TARGET_SETTING targetSetting, IntPtr pData, uint dwDataSize);

        [DllImport("LJX8_IF.dll")]
        internal static extern int LJX8IF_InitializeSetting(int lDeviceId, byte byDepth, byte byTarget);

        [DllImport("LJX8_IF.dll")]
        internal static extern int LJX8IF_ReflectSetting(int lDeviceId, byte byDepth, ref uint pdwError);

        [DllImport("LJX8_IF.dll")]
        internal static extern int LJX8IF_RewriteTemporarySetting(int lDeviceId, byte byDepth);

        [DllImport("LJX8_IF.dll")]
        internal static extern int LJX8IF_CheckMemoryAccess(int lDeviceId, ref byte pbyBusy);

        [DllImport("LJX8_IF.dll")]
        internal static extern int LJX8IF_ChangeActiveProgram(int lDeviceId, byte byProgramNo);

        [DllImport("LJX8_IF.dll")]
        internal static extern int LJX8IF_GetActiveProgram(int lDeviceId, ref byte pbyProgramNo);

        [DllImport("LJX8_IF.dll")]
        internal static extern int LJX8IF_SetXpitch(int lDeviceId, uint dwXpitch);

        [DllImport("LJX8_IF.dll")]
        internal static extern int LJX8IF_GetXpitch(int lDeviceId, ref uint pdwXpitch);

        [DllImport("LJX8_IF.dll")]
        internal static extern int LJX8IF_GetProfile(int lDeviceId, ref LJX8IF_GET_PROFILE_REQUEST pReq,
        ref LJX8IF_GET_PROFILE_RESPONSE pRsp, ref LJX8IF_PROFILE_INFO pProfileInfo, IntPtr pdwProfileData, uint dwDataSize);

        [DllImport("LJX8_IF.dll")]
        internal static extern int LJX8IF_GetBatchProfile(int lDeviceId, ref LJX8IF_GET_BATCH_PROFILE_REQUEST pReq,
        ref LJX8IF_GET_BATCH_PROFILE_RESPONSE pRsp, ref LJX8IF_PROFILE_INFO pProfileInfo,
        IntPtr pdwBatchData, uint dwDataSize);

        [DllImport("LJX8_IF.dll")]
        internal static extern int LJX8IF_GetBatchSimpleArray(int lDeviceId, ref LJX8IF_GET_BATCH_PROFILE_REQUEST pReq,
        ref LJX8IF_GET_BATCH_PROFILE_RESPONSE pRsp, ref LJX8IF_PROFILE_INFO pProfileInfo,
        IntPtr pProfileHeaderArray, IntPtr pHeightProfileArray, IntPtr pLuminanceProfileArray);

        [DllImport("LJX8_IF.dll")]
        internal static extern int LJX8IF_InitializeHighSpeedDataCommunication(
        int lDeviceId, ref LJX8IF_ETHERNET_CONFIG pEthernetConfig, ushort wHighSpeedPortNo,
        HighSpeedDataCallBack pCallBack, uint dwProfileCount, uint dwThreadId);

        [DllImport("LJX8_IF.dll")]
        internal static extern int LJX8IF_InitializeHighSpeedDataCommunicationSimpleArray(
        int lDeviceId, ref LJX8IF_ETHERNET_CONFIG pEthernetConfig, ushort wHighSpeedPortNo,
        HighSpeedDataCallBackForSimpleArray pCallBackSimpleArray, uint dwProfileCount, uint dwThreadId);

        [DllImport("LJX8_IF.dll")]
        internal static extern int LJX8IF_PreStartHighSpeedDataCommunication(
        int lDeviceId, ref LJX8IF_HIGH_SPEED_PRE_START_REQUEST pReq,
        ref LJX8IF_PROFILE_INFO pProfileInfo);

        [DllImport("LJX8_IF.dll")]
        internal static extern int LJX8IF_StartHighSpeedDataCommunication(int lDeviceId);

        [DllImport("LJX8_IF.dll")]
        internal static extern int LJX8IF_StopHighSpeedDataCommunication(int lDeviceId);

        [DllImport("LJX8_IF.dll")]
        internal static extern int LJX8IF_FinalizeHighSpeedDataCommunication(int lDeviceId);
    }
    #endregion
    #endregion

    #region Utility
    /// <summary>
	/// Utility class
	/// </summary>
	static class Utility
    {
        #region Constant
        /// <summary>
        /// value for head temperature display
        /// </summary>
        private const int DivideValueForHeadTemperatureDisplay = 100;
        /// <summary>
        ///  head temperature invalid value
        /// </summary>
        private const int HeadTemperatureInvalidValue = 0xFFFF;
        #endregion

        #region Enum
        /// <summary>
        /// Structure classification
        /// </summary>
        public enum TypeOfStructure
        {
            ProfileHeader,
            ProfileFooter,
        }
        #endregion

        #region Method

        #region Get the byte size

        /// <summary>
        /// Get the byte size of the structure.
        /// </summary>
        /// <param name="type">Structure whose byte size you want to get.</param>
        /// <returns>Byte size</returns>
        public static int GetByteSize(TypeOfStructure type)
        {
            switch (type)
            {
                case TypeOfStructure.ProfileHeader:
                    LJX8IF_PROFILE_HEADER profileHeader = new LJX8IF_PROFILE_HEADER();
                    return Marshal.SizeOf(profileHeader);

                case TypeOfStructure.ProfileFooter:
                    LJX8IF_PROFILE_FOOTER profileFooter = new LJX8IF_PROFILE_FOOTER();
                    return Marshal.SizeOf(profileFooter);
            }

            return 0;
        }
        #endregion

        #region Acquisition of log 

        /// <summary>
        /// Get the string for log output.
        /// </summary>
        /// <param name="profileInfo">profileInfo</param>
        /// <returns>String for log output</returns>
        public static StringBuilder ConvertProfileInfoToLogString(LJX8IF_PROFILE_INFO profileInfo)
        {
            StringBuilder sb = new StringBuilder();

            // Profile information of the profile obtained
            sb.AppendLine(string.Format(@"  Profile Data Num			: {0}", profileInfo.byProfileCount));
            string luminanceOutput = profileInfo.byLuminanceOutput == 0
                ? @"OFF"
                : @"ON";
            sb.AppendLine(string.Format(@"  Luminance output			: {0}", luminanceOutput));
            sb.AppendLine(string.Format(@"  Profile Data Points			: {0}", profileInfo.nProfileDataCount));
            sb.AppendLine(string.Format(@"  X coordinate of the first point	: {0}", profileInfo.lXStart));
            sb.Append(string.Format(@"  X-direction interval		: {0}", profileInfo.lXPitch));

            return sb;
        }

        /// <summary>
        /// Get the string for log output.
        /// </summary>
        /// <param name="response">"Get batch profile" command response</param>
        /// <returns>String for log output</returns>
        public static StringBuilder ConvertBatchProfileResponseToLogString(LJX8IF_GET_BATCH_PROFILE_RESPONSE response)
        {
            StringBuilder sb = new StringBuilder();

            // Profile information of the profile obtained
            sb.AppendLine(string.Format(@"  CurrentBatchNo			: {0}", response.dwCurrentBatchNo));
            sb.AppendLine(string.Format(@"  CurrentBatchProfileCount		: {0}", response.dwCurrentBatchProfileCount));
            sb.AppendLine(string.Format(@"  OldestBatchNo			: {0}", response.dwOldestBatchNo));
            sb.AppendLine(string.Format(@"  OldestBatchProfileCount		: {0}", response.dwOldestBatchProfileCount));
            sb.AppendLine(string.Format(@"  GetBatchNo			: {0}", response.dwGetBatchNo));
            sb.AppendLine(string.Format(@"  GetBatchProfileCount		: {0}", response.dwGetBatchProfileCount));
            sb.AppendLine(string.Format(@"  GetBatchTopProfileNo		: {0}", response.dwGetBatchTopProfileNo));
            sb.AppendLine(string.Format(@"  GetProfileCount			: {0}", response.byGetProfileCount));
            sb.Append(string.Format(@"  CurrentBatchCommited		: {0}", response.byCurrentBatchCommited));

            return sb;
        }

        /// <summary>
        /// Get the string for log output.
        /// </summary>
        /// <param name="response">"Get profile" command response</param>
        /// <returns>String for log output</returns>
        public static StringBuilder ConvertProfileResponseToLogString(LJX8IF_GET_PROFILE_RESPONSE response)
        {
            StringBuilder sb = new StringBuilder();

            sb.AppendLine(string.Format(@"  CurrentProfileNo	: {0}", response.dwCurrentProfileNo));
            sb.AppendLine(string.Format(@"  OldestProfileNo	: {0}", response.dwOldestProfileNo));
            sb.AppendLine(string.Format(@"  GetTopProfileNo	: {0}", response.dwGetTopProfileNo));
            sb.Append(string.Format(@"  GetProfileCount	: {0}", response.byGetProfileCount));

            return sb;
        }

        #endregion

        /// <summary>
        /// Get the string for log output.
        /// </summary>
        /// <param name="sensorTemperature">sensor Temperature</param>
        /// <param name="processorTemperature">processor Temperature</param>
        /// <param name="caseTemperature">case Temperature</param>
        /// <returns>String for log output</returns>
        public static StringBuilder ConvertHeadTemperatureLogString(short sensorTemperature, short processorTemperature, short caseTemperature)
        {
            StringBuilder sb = new StringBuilder();

            sb.AppendLine(string.Format(@"  SensorTemperature	: {0}", GetTemperatureString(sensorTemperature)));
            sb.AppendLine(string.Format(@"  ProcessorTemperature	: {0}", GetTemperatureString(processorTemperature)));
            sb.Append(string.Format(@"  CaseTemperature		: {0}", GetTemperatureString(caseTemperature)));

            return sb;
        }

        private static string GetTemperatureString(short temperature)
        {
            if ((temperature & HeadTemperatureInvalidValue) == HeadTemperatureInvalidValue)
            {
                return "----";
            }
            return string.Format(@"{0} C", (decimal)temperature / DivideValueForHeadTemperatureDisplay);
        }
        #endregion

        #region Get and Set Ethernet setting
        //public static void UpdateTextFromEthernetSetting(LJX8IF_ETHERNET_CONFIG ethernetConfig, TextBox textBoxFirstSegment, TextBox textBoxSecondSegment, TextBox textBoxThirdSegment, TextBox textBoxFourthSegment)
        //{
        //    textBoxFirstSegment.Text = ethernetConfig.abyIpAddress[0].ToString();
        //    textBoxSecondSegment.Text = ethernetConfig.abyIpAddress[1].ToString();
        //    textBoxThirdSegment.Text = ethernetConfig.abyIpAddress[2].ToString();
        //    textBoxFourthSegment.Text = ethernetConfig.abyIpAddress[3].ToString();
        //}

        public static byte[] GetIpAddressFromTextBox(string textBoxFirstSegment, string textBoxSecondSegment, string textBoxThirdSegment, string textBoxFourthSegment)
        {
            return new byte[]
            {
                Convert.ToByte(textBoxFirstSegment),
                Convert.ToByte(textBoxSecondSegment),
                Convert.ToByte(textBoxThirdSegment),
                Convert.ToByte(textBoxFourthSegment)
            };
        }
        #endregion
    }
    #endregion

    #region Define
    /// <summary>
	/// Constant class
	/// </summary>
	public static class Define
    {
        #region Constant

        public enum LjxHeadSamplingPeriod
        {
            LjxHeadSamplingPeriod10Hz = 0,
            LjxHeadSamplingPeriod20Hz,
            LjxHeadSamplingPeriod50Hz,
            LjxHeadSamplingPeriod100Hz,
            LjxHeadSamplingPeriod200Hz,
            LjxHeadSamplingPeriod500Hz,
            LjxHeadSamplingPeriod1KHz,
            LjxHeadSamplingPeriod2KHz,
            LjxHeadSamplingPeriod4KHz,
            LjxHeadSamplingPeriod8KHz,
            LjxHeadSamplingPeriod16KHz,
        }

        public enum LuminanceOutput
        {
            LuminanceOutputOn,
            LuminanceOutputOff
        }

        /// <summary>
        /// Maximum amount of data for 1 profile
        /// </summary>
        public const int MaxProfileCount = LjxHeadMeasureRangeFull;

        /// <summary>
        /// Device ID (fixed to 0)
        /// </summary>
        public const int DeviceId = 0;

        /// <summary>
        /// Maximum profile count that store to buffer.
        /// </summary>
#if WIN64
		public const int BufferFullCount = 120000;
#else
        public const int BufferFullCount = 30000;
#endif
        // @Point
        //  32-bit architecture cannot allocate huge memory and the buffer limit is more strict.

        /// <summary>
        /// Measurement range X direction of LJ-X Head
        /// </summary>
        public const int LjxHeadMeasureRangeFull = 3200;
        public const int LjxHeadMeasureRangeThreeFourth = 2400;
        public const int LjxHeadMeasureRangeHalf = 1600;
        public const int LjxHeadMeasureRangeQuarter = 800;

        /// <summary>
        /// Light reception characteristic
        /// </summary>
        public const int ReceivedBinningOff = 1;
        public const int ReceivedBinningOn = 2;

        public const int ThinningXOff = 1;
        public const int ThinningX2 = 2;
        public const int ThinningX4 = 4;


        /// <summary>
        /// Measurement range X direction of LJ-V Head
        /// </summary>
        public const int MeasureRangeFull = 800;
        public const int MeasureRangeMiddle = 600;
        public const int MeasureRangeSmall = 400;
        #endregion
    }
    #endregion

    #region PinnedObject
    /// <summary>
	/// Object pinning class
	/// </summary>
	public sealed class PinnedObject : IDisposable
    {
        #region Field

        private GCHandle _handle;      // Garbage collector handle

        #endregion

        #region Property

        /// <summary>
        /// Get the address.
        /// </summary>
        public IntPtr Pointer
        {
            // Get the leading address of the current object that is pinned.
            get { return _handle.AddrOfPinnedObject(); }
        }

        #endregion

        #region Constructor

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="target">Target to protect from the garbage collector</param>
        public PinnedObject(object target)
        {
            // Pin the target to protect it from the garbage collector.
            _handle = GCHandle.Alloc(target, GCHandleType.Pinned);
        }

        #endregion

        #region Interface
        /// <summary>
        /// Interface
        /// </summary>
        public void Dispose()
        {
            _handle.Free();
            _handle = new GCHandle();
        }

        #endregion
    }
    #endregion

    #region ThreadSafeBuffer
    public static class ThreadSafeBuffer
    {
        #region Constant
        private const int BatchFinalizeFlagBitCount = 16;
        #endregion

        #region Field
        /// <summary>Data buffer</summary>
        private static List<int[]>[] _buffer = new List<int[]>[NativeMethods.DeviceCount];
        /// <summary>Buffer for the amount of data</summary>
        private static uint[] _count = new uint[NativeMethods.DeviceCount];
        /// <summary>Object for exclusive control</summary>
        private static object[] _syncObject = new object[NativeMethods.DeviceCount];
        /// <summary>Callback function notification parameter</summary>
        private static uint[] _notify = new uint[NativeMethods.DeviceCount];
        /// <summary>Batch number</summary>
        private static int[] _batchNo = new int[NativeMethods.DeviceCount];
        #endregion

        #region Constructor

        /// <summary>
        /// Constructor
        /// </summary>
        static ThreadSafeBuffer()
        {
            for (int i = 0; i < NativeMethods.DeviceCount; i++)
            {
                _buffer[i] = new List<int[]>();
                _syncObject[i] = new object();
            }
        }
        #endregion

        #region Method
        /// <summary>
        /// Get buffer data count
        /// </summary>
        /// <returns>buffer data count</returns>
        public static int GetBufferDataCount(int index)
        {
            return _buffer[index].Count;
        }

        /// <summary>
        /// Element addition
        /// </summary>
        /// <param name="index">User information set when high-speed communication was initialized</param>
        /// <param name="value">Additional element</param>
        /// <param name="notify">Parameter for notification</param>
        public static void Add(int index, List<int[]> value, uint notify)
        {
            lock (_syncObject[index])
            {
                _buffer[index].AddRange(value);
                _count[index] += (uint)value.Count;
                _notify[index] |= notify;
                // Add the batch number if the batch has been finalized.
                if ((uint)(notify & (0x1 << BatchFinalizeFlagBitCount)) != 0) _batchNo[index]++;
            }
        }

        /// <summary>
        /// Clear elements.
        /// </summary>
        /// <param name="index">Device ID</param>
        public static void Clear(int index)
        {
            lock (_syncObject[index])
            {
                _buffer[index].Clear();
            }
        }

        /// <summary>
        /// Clear the buffer.
        /// </summary>
        /// <param name="index">Device ID</param>
        public static void ClearBuffer(int index)
        {
            Clear(index);
            ClearCount(index);
            _batchNo[index] = 0;
            ClearNotify(index);
        }

        /// <summary>
        /// Get element.
        /// </summary>
        /// <param name="index">Device ID</param>
        /// <param name="notify">Parameter for notification</param>
        /// <param name="batchNo">Batch number</param>
        /// <returns>Element</returns>
        public static List<int[]> Get(int index, out uint notify, out int batchNo)
        {
            List<int[]> value = new List<int[]>();
            lock (_syncObject[index])
            {
                value.AddRange(_buffer[index]);
                _buffer[index].Clear();
                notify = _notify[index];
                _notify[index] = 0;
                batchNo = _batchNo[index];
            }
            return value;
        }

        /// <summary>
        /// Add the count
        /// </summary>
        /// <param name="index">Device ID</param>
        /// <param name="count">Count</param>
        /// <param name="notify">Parameter for notification</param>
        internal static void AddCount(int index, uint count, uint notify)
        {
            lock (_syncObject[index])
            {
                _count[index] += count;
                _notify[index] |= notify;
                // Add the batch number if the batch has been finalized.
                if ((uint)(notify & (0x1 << BatchFinalizeFlagBitCount)) != 0) _batchNo[index]++;
            }
        }

        /// <summary>
        /// Get the count
        /// </summary>
        /// <param name="index">Device ID</param>
        /// <param name="notify">Parameter for notification</param>
        /// <param name="batchNo">Batch number</param>
        /// <returns></returns>
        internal static uint GetCount(int index, out uint notify, out int batchNo)
        {
            lock (_syncObject[index])
            {
                notify = _notify[index];
                _notify[index] = 0;
                batchNo = _batchNo[index];
                return _count[index];
            }
        }

        /// <summary>
        /// Clear the number of elements.
        /// </summary>
        /// <param name="index">Device ID</param>
        private static void ClearCount(int index)
        {
            lock (_syncObject[index])
            {
                _count[index] = 0;
            }
        }

        /// <summary>
        /// Clear notifications.
        /// </summary>
        /// <param name="index">Device ID</param>
        private static void ClearNotify(int index)
        {
            lock (_syncObject[index])
            {
                _notify[index] = 0;
            }
        }

        #endregion
    }
    #endregion
}
