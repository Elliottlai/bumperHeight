//using DocumentFormat.OpenXml.Office2010.Excel;
//using FoupInspecMachine.Helper;
//using HalconDotNet;
using FoupInspecMachine.Helper;
using Machine.Core;
using Machine.Core.Interfaces;

using Slot_Inspection.Models;

//using SharedProject.Helpers;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace FoupInspecMachine.Manager
{
    using M = cMachineManager;
    using Point = System.Drawing.Point;
    public class NamedKeyCamera
    {
        //public static NamedKey LineCameraLeft { get; } = NamedKey.Create();
        //public static NamedKey LineCameraRight { get; } = NamedKey.Create();
        //public static NamedKey AreaCameraTop { get; } = NamedKey.Create();
        //public static NamedKey AreaCameraSide { get; } = NamedKey.Create();


        //public static NamedKey AreaCameraHandle { get; } = NamedKey.Create();

        //public static NamedKey AreaCameraDoorSide { get; } = NamedKey.Create();

        //public static NamedKey AreaCameraTopFilter { get; } = NamedKey.Create();

        //public static NamedKey AreaCameraBottomFilter { get; } = NamedKey.Create();
        //public static NamedKey AreaCameraDoor  { get; } = NamedKey.Create();
        //public static NamedKey AreaCameraCarrier { get; } = NamedKey.Create();

        //   public static eKey VRSCamera { get; } = eKey.Create();

        public static NamedKey LineCameraLeft { get; } = NamedKey.Create();
        public static NamedKey LineCameraRight { get; } = NamedKey.Create();
        public static NamedKey AreaCameraTop { get; } = NamedKey.Create();
        public static NamedKey AreaCameraSide { get; } = NamedKey.Create();
        public static NamedKey FoupTop { get; } = NamedKey.Create();
        public static NamedKey FoupSide { get; } = NamedKey.Create();
        public static NamedKey DoorBack { get; } = NamedKey.Create();
        public static NamedKey Foup { get; } = NamedKey.Create();
        public static NamedKey Valve { get; } = NamedKey.Create();
        public static NamedKey DoorSide { get; } = NamedKey.Create();
        public static NamedKey FilterTop { get; } = NamedKey.Create();
        public static NamedKey HandleCrack { get; } = NamedKey.Create();


        public static readonly IReadOnlyList<(string Name, NamedKey Key)> All = new[]
{
        (nameof(LineCameraLeft),  LineCameraLeft),
        (nameof(LineCameraRight), LineCameraRight),
        (nameof(AreaCameraTop),   AreaCameraTop),
        (nameof(AreaCameraSide),  AreaCameraSide),
        (nameof(FoupTop),         FoupTop),
        (nameof(FoupSide),        FoupSide),
        (nameof(DoorBack),        DoorBack),
        (nameof(Foup),            Foup),
        (nameof(Valve),           Valve),
        (nameof(DoorSide),        DoorSide),
        (nameof(FilterTop),       FilterTop),
        (nameof(HandleCrack),     HandleCrack),
    };

    }
    class CameraData
    {
        public ICamera camera;
        public HImage[] ImagePools;
        public ConcurrentQueue<int> ImageBufferKeys;
        public int BufID;
        public IntPtr[] ImagePtr;

    }
    public class CameraManager
    {
        Dictionary<NamedKey, CameraData> CameraDataDict { get; set; } = new Dictionary<NamedKey, CameraData>();

        // ─── 模擬圖片 ───
        private readonly Dictionary<NamedKey, SimImagePool> _simPools = new();
        private readonly Dictionary<NamedKey, HImage> _simCurrentImages = new();
        private string _simImageRoot;

        /// <summary>
        /// 相機 Key → 子資料夾名稱的對應表。
        /// 預設使用 NamedKeyCamera 的屬性名，可透過 SetSimFolderMapping 覆寫。
        /// </summary>
        private readonly Dictionary<NamedKey, string> _simFolderMap = new();

        private bool _isSimulatio;

        public bool isSimulaion
        {
            get { return _isSimulatio; }
            set { _isSimulatio = value; }
        }

        public CameraManager(bool _isSimulation)

        {
            isSimulaion = _isSimulation;

            // 初始化預設資料夾對應（與 NamedKeyCamera 屬性名一致）
            foreach (var (name, key) in NamedKeyCamera.All)
                _simFolderMap[key] = name;

            if (isSimulaion) return;
            var keys = typeof(NamedKeyCamera)
    .GetProperties(BindingFlags.Public | BindingFlags.Static)
    .Where(p => p.PropertyType == typeof(NamedKey))
    .Select(p => p.GetValue(null) as NamedKey);


            foreach (var name in keys)
            {
                if (name == NamedKeyCamera.AreaCameraTop || name == NamedKeyCamera.AreaCameraSide) { continue; }
                try
                {
                    M.Cameras[name].Init();
                    CameraDataDict.Add(name, new CameraData() { camera = M.Cameras[name] });
                }
                catch (Exception e)
                {
                    Nlogger.Debug($"Camera Init  {name} Error");
                }
            }

            InitImageBuffer(cGM_Dalsa_1.TotalGrabCount);
        }

        // ─── 模擬資料夾對應設定 ───

        /// <summary>
        /// 設定單一相機的模擬圖片子資料夾名稱。
        /// 例如：SetSimFolderMapping(NamedKeyCamera.FoupSide, "CameraDoorHandle")
        /// </summary>
        public void SetSimFolderMapping(NamedKey cameraKey, string folderName)
        {
            _simFolderMap[cameraKey] = folderName;
        }

        /// <summary>
        /// 批次設定多個相機的模擬圖片子資料夾名稱。
        /// </summary>
        public void SetSimFolderMappings(Dictionary<NamedKey, string> mappings)
        {
            foreach (var (key, folder) in mappings)
                _simFolderMap[key] = folder;
        }

        /// <summary>取得目前的資料夾對應表（唯讀）</summary>
        public IReadOnlyDictionary<NamedKey, string> SimFolderMappings
            => _simFolderMap;

        // ─── 模擬圖片載入 ───

        /// <summary>
        /// 設定模擬圖片根目錄，自動掃描子資料夾對應各相機。
        /// 資料夾結構：rootPath\{自訂資料夾名}\*.tif|*.png|*.bmp|*.jpg
        /// </summary>
        public void LoadSimulationImages(string rootPath)
        {
            _simImageRoot = rootPath;
            _simPools.Clear();

            foreach (var img in _simCurrentImages.Values)
                img?.Dispose();
            _simCurrentImages.Clear();

            if (!Directory.Exists(rootPath))
            {
                Nlogger.Debug($"SimImage: 根目錄不存在 {rootPath}");
                return;
            }

            // 1. 掃描所有圖片
            string[] extensions = ["*.tif", "*.png", "*.bmp", "*.jpg"];
            var allFiles = extensions
                .SelectMany(ext => Directory.GetFiles(rootPath, ext, SearchOption.AllDirectories))
                .ToArray();

            Nlogger.Debug($"SimImage: 掃描到 {allFiles.Length} 張圖片");

            // 2. 每張圖片的檔名比對 suffix → Camera
            var cameraFiles = new Dictionary<NamedKey, List<(string Path, DateTime Time)>>();

            foreach (var file in allFiles)
            {
                var nameNoExt = Path.GetFileNameWithoutExtension(file);

                // 找到最後一個 _ 後的部分作為 suffix
                // 檔名格式: {date}_Exp{n}_{material}_{foupId}_{suffix}.tif
                var lastUnderscore = nameNoExt.LastIndexOf('_');
                if (lastUnderscore < 0) continue;
                var suffix = nameNoExt[(lastUnderscore + 1)..];

                var camera = SimSuffixCameraMap.Resolve(suffix);
                if (camera == null)
                {
                    Nlogger.Debug($"SimImage: 未匹配 suffix '{suffix}' → {Path.GetFileName(file)}");
                    continue;
                }

                if (!cameraFiles.ContainsKey(camera))
                    cameraFiles[camera] = [];

                cameraFiles[camera].Add((file, File.GetLastWriteTime(file)));
            }

            // 3. 依時間排序 → 建立 Pool
            foreach (var (camera, files) in cameraFiles)
            {
                var sorted = files.OrderBy(f => f.Time).Select(f => f.Path).ToArray();
                _simPools[camera] = new SimImagePool(sorted);

                var name = NamedKeyCamera.All.FirstOrDefault(x => x.Key == camera).Name ?? "?";
                Nlogger.Debug($"SimImage: {name} → {sorted.Length} 張");
            }

            // 4. 報告缺少的 Camera
            foreach (var (name, key) in NamedKeyCamera.All)
            {
                if (!_simPools.ContainsKey(key))
                    Nlogger.Debug($"SimImage: {name} 無對應圖片");
            }
        }

        /// <summary>模擬圖片根目錄</summary>
        public string SimImageRoot => _simImageRoot;

        /// <summary>取得指定相機的模擬圖片張數</summary>
        public int GetSimImageCount(NamedKey camera)
            => _simPools.TryGetValue(camera, out var pool) ? pool.Count : 0;

        /// <summary>重設所有模擬圖片索引</summary>
        public void ResetSimImageIndex()
        {
            foreach (var pool in _simPools.Values)
                pool.Reset();
        }

        /// <summary>
        /// 從模擬圖片池讀取下一張圖（循環），並快取為 HImage
        /// </summary>
        private HImage GetSimImage(NamedKey camera)
        {
            if (!_simPools.TryGetValue(camera, out var pool)) return null;

            var filePath = pool.Next();
            if (filePath == null) return null;

            try
            {
                if (_simCurrentImages.TryGetValue(camera, out var oldImg))
                    oldImg.Dispose();

                var img = new HImage();
                img.ReadImage(filePath);
                _simCurrentImages[camera] = img;
                return img;
            }
            catch (Exception ex)
            {
                Nlogger.Debug($"SimImage: 讀取失敗 {filePath} - {ex.Message}");
                return null;
            }
        }

        /// <summary>取得目前快取的模擬圖（不前進索引）</summary>
        private HImage GetSimCurrentImage(NamedKey camera)
            => _simCurrentImages.TryGetValue(camera, out var img) ? img : null;

        private string GetSimFolderName(NamedKey key)
            => _simFolderMap.TryGetValue(key, out var name) ? name : "Unknown";

        // ─── 以下為原有方法，修改 GetCameraImage 和 SaveCameraImage ───

        public unsafe void InitImageBuffer(int Size)
        {
            foreach (var cameraData in CameraDataDict)
            {

                // Init Pools
                cameraData.Value.ImagePools = new HImage[Size];
                cameraData.Value.ImagePtr = new IntPtr[Size];
                cameraData.Value.ImageBufferKeys = new ConcurrentQueue<int>();

                // SetDatas
                const string type = "byte";
                for (int j = 0; j < cameraData.Value.ImagePools.Length; j++)
                {
                    try
                    {
                        // Key
                        cameraData.Value.ImageBufferKeys.Enqueue(j);
                        // Buffer
                        IntPtr[] RGB = cameraData.Value.camera.GetBufAddress(j);
                        cameraData.Value.ImagePools[j] = new HImage();
                        //if (RGB.Length == 3)
                        //    cameraData.Value.ImagePools[j].GenImage3Extern(type, cameraData.Value.camera.FrameWidth, cameraData.Value.camera.BufHeight, RGB[0], RGB[1], RGB[2], IntPtr.Zero);
                        //else
                        cameraData.Value.ImagePools[j].GenImage1Extern(type, cameraData.Value.camera.FrameWidth, cameraData.Value.camera.BufHeight, RGB[0], IntPtr.Zero);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"{cameraData}");
                    }
                }
            }


        }
        #region GetCameraImage

        public HImage GetCameraImage(NamedKey camera, int bufid = -1)
        {
            if (isSimulaion)
                return GetSimCurrentImage(camera);

            if (CameraDataDict.ContainsKey(camera))
            {
                int id = bufid;
                if (bufid == -1)
                    id = ((cGM_Dalsa_1)CameraDataDict[camera].camera).GetCurrentBufID();
                return CameraDataDict[camera].ImagePools[id];
            }
            return null;
        }

        public HImage GetAreaCameraSideImage(int bufid = -1) => GetCameraImage(NamedKeyCamera.AreaCameraSide, bufid);
        public HImage GetAreaCameraTopImage(int bufid = -1) => GetCameraImage(NamedKeyCamera.AreaCameraTop, bufid);
        public HImage GetDoorBackImage(int bufid = -1) => GetCameraImage(NamedKeyCamera.DoorBack);
        public HImage GetFoupImage(int bufid = -1) => GetCameraImage(NamedKeyCamera.Foup);
        public HImage GetValveImage(int bufid = -1) => GetCameraImage(NamedKeyCamera.Valve);
        public HImage GetFoupTopImage(int bufid = -1) => GetCameraImage(NamedKeyCamera.FoupTop);
        public HImage GetDoorSideImage(int bufid = -1) => GetCameraImage(NamedKeyCamera.DoorSide);
        public HImage GetFilterTopImage(int bufid = -1) => GetCameraImage(NamedKeyCamera.FilterTop);
        public HImage GetFoupSideImage(int bufid = -1) => GetCameraImage(NamedKeyCamera.FoupSide);
        public HImage GetHandleCrackImage(int bufid = -1) => GetCameraImage(NamedKeyCamera.HandleCrack);
        public HImage GetLineCameraRightImage(int bufid = -1) => GetCameraImage(NamedKeyCamera.LineCameraRight, bufid);
        public HImage GetLineCameraLeftImage(int bufid = -1) => GetCameraImage(NamedKeyCamera.LineCameraLeft, bufid);

        #endregion GetCameraImage

        #region CameraStart

        public void CameraStart(NamedKey camera)
        {
            if (isSimulaion) return;
            if (CameraDataDict.ContainsKey(camera))
                CameraDataDict[camera].camera.Start();
        }

        public void AreaCameraSideStart() => CameraStart(NamedKeyCamera.AreaCameraSide);
        public void AreaCameraTopStart() => CameraStart(NamedKeyCamera.AreaCameraTop);
        public void DoorBackStart() => CameraStart(NamedKeyCamera.DoorBack);
        public void FoupStart() => CameraStart(NamedKeyCamera.Foup);
        public void ValveStart() => CameraStart(NamedKeyCamera.Valve);
        public void FoupTopStart() => CameraStart(NamedKeyCamera.FoupTop);
        public void DoorSideStart() => CameraStart(NamedKeyCamera.DoorSide);
        public void FilterTopStart() => CameraStart(NamedKeyCamera.FilterTop);
        public void FoupSideStart() => CameraStart(NamedKeyCamera.FoupSide);
        public void HandleCrackStart() => CameraStart(NamedKeyCamera.HandleCrack);

        #endregion

        #region CameraStop

        public void CameraeStop(NamedKey key)
        {
            if (isSimulaion) return;
            if (CameraDataDict.ContainsKey(key))
                CameraDataDict[key].camera.Stop();
        }

        public void AreaCameraSideStop() => CameraeStop(NamedKeyCamera.AreaCameraSide);
        public void AreaCameraTopStop() => CameraeStop(NamedKeyCamera.AreaCameraTop);
        public void DoorBackStop() => CameraeStop(NamedKeyCamera.DoorBack);
        public void FoupStop() => CameraeStop(NamedKeyCamera.Foup);
        public void ValveStop() => CameraeStop(NamedKeyCamera.Valve);
        public void FoupTopStop() => CameraeStop(NamedKeyCamera.FoupTop);
        public void DoorSideStop() => CameraeStop(NamedKeyCamera.DoorSide);
        public void FilterTopStop() => CameraeStop(NamedKeyCamera.FilterTop);
        public void FoupSideStop() => CameraeStop(NamedKeyCamera.FoupSide);
        public void HandleCrackStop() => CameraeStop(NamedKeyCamera.HandleCrack);
        public void LineCameraLeftStop() => CameraeStop(NamedKeyCamera.LineCameraLeft);
        public void LineCameraRightStop() => CameraeStop(NamedKeyCamera.LineCameraRight);

        #endregion CameraStop

        #region SetExposure

        public void CameraSetExposure(NamedKey key, double exposure)
        {
            if (isSimulaion) return;
            if (CameraDataDict.ContainsKey(key))
            {
                CameraeStop(key);
                CameraDataDict[key].camera.SetFeatureValue(GMExpandParamter.ExposureTime, exposure);
                CameraStart(key);
            }
        }

        public void AreaCameraTopSetExposure(double exposure) => CameraSetExposure(NamedKeyCamera.AreaCameraTop, exposure);
        public void AreaCameraSideSetExposure(double exposure) => CameraSetExposure(NamedKeyCamera.AreaCameraSide, exposure);
        public void DoorBackSetExposure(double exposure) => CameraSetExposure(NamedKeyCamera.DoorBack, exposure);
        public void FoupSetExposure(double exposure) => CameraSetExposure(NamedKeyCamera.Foup, exposure);
        public void ValveSetExposure(double exposure) => CameraSetExposure(NamedKeyCamera.Valve, exposure);
        public void FoupTopSetExposure(double exposure) => CameraSetExposure(NamedKeyCamera.FoupTop, exposure);
        public void DoorSideExposure(double exposure) => CameraSetExposure(NamedKeyCamera.DoorSide, exposure);
        public void FilterTopSetExposure(double exposure) => CameraSetExposure(NamedKeyCamera.FilterTop, exposure);
        public void FoupSideSetExposure(double exposure) => CameraSetExposure(NamedKeyCamera.FoupSide, exposure);
        public void HandleCrackSetExposure(double exposure) => CameraSetExposure(NamedKeyCamera.HandleCrack, exposure);
        public void Line1SetExposure(double exposure) => CameraSetExposure(NamedKeyCamera.LineCameraLeft, exposure);
        public void Line2SetExposure(double exposure) => CameraSetExposure(NamedKeyCamera.LineCameraRight, exposure);

        #endregion SetExposure  

        #region SaveImage

        public void SaveCameraImage(NamedKey key, string fileName, string label1, string label2, int bufid)
        {
            if (isSimulaion)
            {
                SaveSimImage(key, fileName, label1, label2);
                return;
            }
            GetCameraImage(key, bufid).WriteImage("tiff", 0, fileName);
            TiffTagWriter.WriteTags(fileName, label1, label2);
        }

        public void SaveCameraImage(NamedKey camera, string filename, string label1 = "", string label2 = "", int bufid = -1, int x = -1, int y = -1, int w = -1, int h = -1)
        {
            if (isSimulaion)
            {
                SaveSimImage(camera, filename, label1, label2, x, y, w, h);
                return;
            }
            if (CameraDataDict.ContainsKey(camera))
            {
                int id = bufid;
                if (bufid == -1)
                    id = ((cGM_Dalsa_1)CameraDataDict[camera].camera).GetCurrentBufID();

                if (x == -1 || y == -1 || w == -1 || h == -1)
                    CameraDataDict[camera].ImagePools[id].WriteImage("tiff", 0, filename);
                else
                {
                    var img = CameraDataDict[camera].ImagePools[id].CropPart(y, x, w, h);
                    img.WriteImage("tiff", 0, filename);
                    img.Dispose();
                }
                TiffTagWriter.WriteTags(filename, label1, label2);
            }
        }

        /// <summary>
        /// 模擬模式：從圖片池取下一張圖，寫入目標路徑
        /// </summary>
        private void SaveSimImage(NamedKey camera, string destFileName, string label1, string label2, int x = -1, int y = -1, int w = -1, int h = -1)
        {
            var img = GetSimImage(camera);
            if (img == null)
            {
                Nlogger.Debug($"SimImage: {GetSimFolderName(camera)} 無可用圖片，跳過存檔");
                return;
            }

            try
            {
                var dir = Path.GetDirectoryName(destFileName);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                if (x == -1 || y == -1 || w == -1 || h == -1)
                {
                    img.WriteImage("tiff", 0, destFileName);
                }
                else
                {
                    var cropped = img.CropPart(y, x, w, h);
                    cropped.WriteImage("tiff", 0, destFileName);
                    cropped.Dispose();
                }

                TiffTagWriter.WriteTags(destFileName, label1, label2);

                var srcFile = _simPools.TryGetValue(camera, out var pool) ? pool.Current : "unknown";
                Nlogger.Debug($"SimImage: {GetSimFolderName(camera)} → {Path.GetFileName(destFileName)} (來源: {Path.GetFileName(srcFile)})");
            }
            catch (Exception ex)
            {
                Nlogger.Debug($"SimImage: 存檔失敗 {destFileName} - {ex.Message}");
            }
        }

        public void DoorBackSaveImage(string fileName, string label1 = "", string label2 = "", int bufid = -1)
            => SaveCameraImage(NamedKeyCamera.DoorBack, fileName, label1, label2, bufid);
        public void DoorSideSaveImage(string fileName, string label1 = "", string label2 = "", int bufid = -1)
            => SaveCameraImage(NamedKeyCamera.DoorSide, fileName, label1, label2, bufid);
        public void FilterTopSaveImage(string fileName, string label1 = "", string label2 = "", int bufid = -1)
            => SaveCameraImage(NamedKeyCamera.FilterTop, fileName, label1, label2, bufid);
        public void FoupSaveImage(string fileName, string label1 = "", string label2 = "", int bufid = -1)
            => SaveCameraImage(NamedKeyCamera.Foup, fileName, label1, label2, bufid);
        public void FoupSideSaveImage(string fileName, string label1 = "", string label2 = "", int bufid = -1)
            => SaveCameraImage(NamedKeyCamera.FoupSide, fileName, label1, label2, bufid);
        public void FoupTopSaveImage(string fileName, string label1 = "", string label2 = "", int bufid = -1)
            => SaveCameraImage(NamedKeyCamera.FoupTop, fileName, label1, label2, bufid);
        public void HandleCrackSaveImage(string fileName, string label1 = "", string label2 = "", int bufid = -1)
            => SaveCameraImage(NamedKeyCamera.HandleCrack, fileName, label1, label2, bufid);
        public void LineLeftSaveImage(string fileName, string label1 = "", string label2 = "", int bufid = -1)
            => SaveCameraImage(NamedKeyCamera.LineCameraLeft, fileName, label1, label2, bufid);
        public void LineRightSaveImage(string fileName, string label1 = "", string label2 = "", int bufid = -1)
            => SaveCameraImage(NamedKeyCamera.LineCameraRight, fileName, label1, label2, bufid);
        public void ValveSaveImage(string fileName, string label1 = "", string label2 = "", int bufid = -1)
            => SaveCameraImage(NamedKeyCamera.Valve, fileName, label1, label2, bufid);

        #endregion


        public void LineCameraRightStart(int bufindex = -1)
        {
            if (isSimulaion) return;
            LineCameraStart(NamedKeyCamera.LineCameraRight, bufindex);
        }
        public void LineCameraLeftStart(int bufindex = -1)
        {
            if (isSimulaion) return;
            LineCameraStart(NamedKeyCamera.LineCameraLeft, bufindex);
        }
        public void LineCameraStart(NamedKey camera, int bufindex)
        {
            if (CameraDataDict.ContainsKey(camera))
            {
                if (bufindex == -1)
                    CameraDataDict[camera].camera.Start();
                else
                    CameraDataDict[camera].camera.Start(bufindex);
            }
        }

        public void SaveAreaCameraTopImage(string filename, int bufid = -1, int x = -1, int y = -1, int w = -1, int h = -1) { }
        public void SaveAreaCameraSideImage(string filename, int bufid = -1, int x = -1, int y = -1, int w = -1, int h = -1) { }
        public void SaveLineCameraLeftImage(string filename, int bufid = -1, string label1 = "", string label2 = "", int x = -1, int y = -1, int w = -1, int h = -1)
            => SaveCameraImage(NamedKeyCamera.LineCameraLeft, filename, label1, label2, bufid, x, y, w, h);
        public void SaveLineCameraRightImage(string filename, int bufid = -1, string label1 = "", string label2 = "", int x = -1, int y = -1, int w = -1, int h = -1)
            => SaveCameraImage(NamedKeyCamera.LineCameraRight, filename, label1, label2, bufid, x, y, w, h);
    }
}
    /// <summary>
    /// 模擬用圖片池：從子資料夾循環讀圖
    /// </summary>
    class SimImagePool
    {
        private readonly string[] _files;
        private int _index;

        public SimImagePool(string[] files)
        {
            _files = files;
            _index = 0;
        }

        public int Count => _files.Length;

        /// <summary>取得下一張圖片路徑（循環）</summary>
        public string Next()
        {
            if (_files.Length == 0) return null;
            var file = _files[_index];
            _index = (_index + 1) % _files.Length;
            return file;
        }

        /// <summary>取得當前圖片路徑（不前進）</summary>
        public string Current => _files.Length > 0
            ? _files[(_index - 1 + _files.Length) % _files.Length]
            : null;

        /// <summary>重設索引</summary>
        public void Reset() => _index = 0;
    }