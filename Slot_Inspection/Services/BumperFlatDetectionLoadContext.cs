using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using NLog;

namespace Slot_Inspection.Services;

/// <summary>
/// 私有 AssemblyLoadContext，讓 BumperFlatDetection.dll 用自己那份 Emgu.CV（4.8.1.5350）
/// 跟主程式 NuGet 參照的 Emgu.CV（4.12.0.5764）在同一個 process 裡並存、互不干擾。
///
/// 背景：兩邊都叫 Emgu.CV.dll，如果放在同一個輸出目錄由預設 AssemblyLoadContext 載入，
/// 版本較新的會蓋掉舊的，導致 BumperFlatDetection.dll 內部用到 Emgu.CV 的地方對不上版本，
/// InitializeAsync 不會拋例外，只會靜默回傳 false（見 BumperAlgService.EnsureNewDetectorReadyAsync）。
///
/// 每一次組件/原生 DLL 解析都寫 log，因為 BumperFlatDetector.InitializeAsync()
/// 失敗時只回傳 bool、不會說原因——這是唯一能看到「哪個依賴解析失敗」的地方。
/// </summary>
internal sealed class BumperFlatDetectionLoadContext : AssemblyLoadContext
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
    private readonly string _privateDir;

    public BumperFlatDetectionLoadContext(string privateDir)
        : base(name: "BumperFlatDetectionPrivate", isCollectible: false)
    {
        _privateDir = privateDir;
    }

    /// <summary>
    /// 只攔截私有資料夾裡實際存在的組件（BumperFlatDetection.dll 跟它綁定的那份 Emgu.CV 等）。
    /// 其餘一律回傳 null，交還給預設解析機制——string/Task/List/Dictionary/System.Drawing.* 這些
    /// 需要跨這個 ALC 邊界共用的型別，必須維持同一份身分，不能也被我們攔截、各載一份。
    /// </summary>
    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name == null)
            return null;

        string candidate = Path.Combine(_privateDir, assemblyName.Name + ".dll");
        bool found = File.Exists(candidate);
        _logger.Debug($"[BumperALC] Load 組件請求：{assemblyName.FullName} → " +
            (found ? $"從私有資料夾載入 {candidate}" : "私有資料夾沒有這個檔案，交還預設解析機制"));

        if (!found)
            return null;

        try
        {
            return LoadFromAssemblyPath(candidate);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, $"[BumperALC] LoadFromAssemblyPath 失敗：{candidate}");
            throw;
        }
    }

    /// <summary>
    /// 原生 DLL（cvextern/onnxruntime/cudnn 等）也要優先從這個私有資料夾找，
    /// 避免撿到主程式輸出根目錄裡版本不同的同名原生 DLL。
    /// </summary>
    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        string fileName = unmanagedDllName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            ? unmanagedDllName
            : unmanagedDllName + ".dll";
        string candidate = Path.Combine(_privateDir, fileName);
        bool found = File.Exists(candidate);
        _logger.Debug($"[BumperALC] LoadUnmanagedDll 原生 DLL 請求：{unmanagedDllName} → " +
            (found ? $"從私有資料夾載入 {candidate}" : $"私有資料夾沒有 {fileName}，交還預設解析機制（可能導致 DllNotFoundException）"));

        return found ? LoadUnmanagedDllFromPath(candidate) : IntPtr.Zero;
    }
}
