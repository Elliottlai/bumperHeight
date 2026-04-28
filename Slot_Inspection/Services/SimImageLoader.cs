using System.IO;
using System.Windows.Media.Imaging;
using System.Windows.Media;

namespace Slot_Inspection.Services;

/// <summary>
/// Loads test images from a folder and cycles through them.
/// Pure WPF API, no Halcon dependency.
/// </summary>
public sealed class SimImageLoader
{
    private readonly string[] _files;
    private int _index;

    /// <summary>Whether any images are available</summary>
    public bool HasImages => _files.Length > 0;

    /// <summary>Total image count</summary>
    public int Count => _files.Length;

    private static readonly string[] SupportedExtensions =
        [".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff"];

    public SimImageLoader(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
        {
            _files = [];
            System.Diagnostics.Debug.WriteLine(
                $"[SimImageLoader] Folder missing: {folderPath}, will use generated images");
            return;
        }

        _files = Directory
            .GetFiles(folderPath)
            .Where(f => SupportedExtensions.Contains(
                Path.GetExtension(f).ToLowerInvariant()))
            .OrderBy(f => f)
            .ToArray();

        System.Diagnostics.Debug.WriteLine(
            $"[SimImageLoader] Loaded {_files.Length} images from {folderPath}");
    }

    /// <summary>
    /// Get next image path (cycles).
    /// Returns null if no images.
    /// </summary>
    public string? NextPath()
    {
        if (_files.Length == 0) return null;

        string filePath = _files[_index];
        _index = (_index + 1) % _files.Length;
        return filePath;
    }

    /// <summary>
    /// Get next image (cycles). Frozen for cross-thread safety.
    /// Returns null if load fails.
    /// </summary>
    public BitmapSource? Next()
    {
        if (_files.Length == 0) return null;

        string filePath = _files[_index];
        _index = (_index + 1) % _files.Length;

        return LoadFile(filePath);
    }

    /// <summary>Reset index (start from first image for new batch)</summary>
    public void Reset() => _index = 0;

    /// <summary>
    /// 公開靜態方法：載入指定路徑的圖片為 BitmapSource（已 Freeze）。
    /// 供 MachineController fallback 使用。
    /// </summary>
    public static BitmapSource? LoadFileAsBitmapSource(string filePath)
        => LoadFile(filePath);

    private static BitmapSource? LoadFile(string filePath)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(filePath, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[SimImageLoader] Failed to load {Path.GetFileName(filePath)}: {ex.Message}");
            return null;
        }
    }
}
