using BumperFlat.ImageProcessing;
using BumperFlat.ImageProcessing;
using BumperFlat.ImageProcessing.Models;
using Emgu.CV;
using Emgu.CV.Structure;
using NLog;
using System.Collections;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Slot_Inspection.Services;

/// <summary>
/// 橋接 BumperFlat.ImageProcessing.dll 與 Slot_Inspection。
/// 流程：讀取已存檔影像 → 呼叫 DLL 分析 → 畫輪廓線/瑕疵疊加 → 回傳 BitmapSource。
/// </summary>
public sealed class BumperAlgService
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

    private readonly string _configDir;

    public BumperAlgService(string? configDir = null)
    {
        _configDir = configDir
            ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config");
    }

    /// <summary>
    /// 對已存檔的影像執行演算法分析，並回傳疊加結果的 BitmapSource。
    /// </summary>
    /// <param name="imagePath">已存檔的影像路徑（.tif）</param>
    /// <param name="jsonKey">Config 資料夾下的 JSON 檔名（不含 .json），例如 "Parameter"</param>
    /// <param name="slotName">用於 Log 的 Slot 名稱</param>
    public BumperAlgResult Analyze(string imagePath, string jsonKey, string slotName)
    {
        try
        {
            string jsonPath = Path.Combine(_configDir, jsonKey + ".json");
            System.Diagnostics.Debug.WriteLine(
                $"[BumperAlg] {slotName}: jsonKey={jsonKey}, jsonPath={jsonPath}, jsonExists={File.Exists(jsonPath)}");

            if (!File.Exists(jsonPath))
            {
                System.Diagnostics.Debug.WriteLine($"[BumperAlg] {slotName}: ? JSON 不存在");
                return BumperAlgResult.Fail($"JSON 不存在: {jsonKey}.json");
            }

            if (!File.Exists(imagePath))
            {
                System.Diagnostics.Debug.WriteLine($"[BumperAlg] {slotName}: ? 影像不存在");
                return BumperAlgResult.Fail($"影像不存在: {imagePath}");
            }

            var processor = new ImageProcessor();
            System.Diagnostics.Debug.WriteLine($"[BumperAlg] {slotName}: 呼叫 ProcessImage...");
            ProcessingResult result = processor.ProcessImage(imagePath, jsonPath, false);

            System.Diagnostics.Debug.WriteLine(
                $"[BumperAlg] {slotName}: ProcessImage success={result.Success}, error={result.ErrorMessage ?? "null"}");

            if (!result.Success)
            {
                System.Diagnostics.Debug.WriteLine($"[BumperAlg] {slotName}: ? ProcessImage 失敗");
                return BumperAlgResult.Fail(result.ErrorMessage ?? "ProcessImage failed");
            }

            using Mat baseMat = CvInvoke.Imread(imagePath, Emgu.CV.CvEnum.ImreadModes.AnyColor);
            if (baseMat == null || baseMat.IsEmpty)
            {
                System.Diagnostics.Debug.WriteLine($"[BumperAlg] {slotName}: ? Imread 失敗");
                return BumperAlgResult.Fail("無法讀取原圖");
            }

            // ── 在原圖上畫綠色輪廓線 ──
            using Mat overlay = baseMat.Clone();
            var mainData = result.AdditionalData as Dictionary<string, object>;
            int drawn = DrawContourFromData(overlay, mainData);

            System.Diagnostics.Debug.WriteLine(
                $"[BumperAlg] {slotName}: ? 成功 size={baseMat.Width}x{baseMat.Height}, " +
                $"additionalKeys={mainData?.Count ?? 0}, contourDrawn={drawn}");

            bool isNg = DetermineNg(result);
            var bitmapSource = MatToBitmapSource(overlay);
            bitmapSource.Freeze();
            return new BumperAlgResult(true, isNg, isNg ? "NG" : "OK", bitmapSource);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BumperAlg] {slotName}: ? 例外: {ex.Message}");
            return BumperAlgResult.Fail(ex.Message);
        }
    }

    // ─────────────────────────────────────────────
    //  Private helpers
    // ─────────────────────────────────────────────

    private static Mat? LoadMatFromResult(ProcessingResult result, string originalPath)
    {
        if (result.ProcessedImageData is { Length: > 0 })
        {
            var mat = new Mat();
            CvInvoke.Imdecode(result.ProcessedImageData, Emgu.CV.CvEnum.ImreadModes.AnyColor, mat);
            return mat.IsEmpty ? null : mat;
        }

        if (!string.IsNullOrEmpty(result.ProcessedImagePath)
            && File.Exists(result.ProcessedImagePath))
            return CvInvoke.Imread(result.ProcessedImagePath);

        return CvInvoke.Imread(originalPath);
    }

    private static bool TryConvertPoint(object? source, out System.Drawing.Point point)
    {
        if (source is System.Drawing.Point p)
        {
            point = p;
            return true;
        }

        if (source is System.Drawing.PointF pf)
        {
            point = new System.Drawing.Point((int)Math.Round(pf.X), (int)Math.Round(pf.Y));
            return true;
        }

        if (source is int[] ia && ia.Length >= 2)
        {
            point = new System.Drawing.Point(ia[0], ia[1]);
            return true;
        }

        if (source is float[] fa && fa.Length >= 2)
        {
            point = new System.Drawing.Point((int)Math.Round(fa[0]), (int)Math.Round(fa[1]));
            return true;
        }

        if (source is double[] da && da.Length >= 2)
        {
            point = new System.Drawing.Point((int)Math.Round(da[0]), (int)Math.Round(da[1]));
            return true;
        }

        point = default;
        return false;
    }

    private static int DrawContourFromData(Mat target, Dictionary<string, object>? data)
    {
        if (data == null) return 0;

        // 兼容不同 DLL 欄位名稱（Contour / DefectContour / xxxContour）
        var contourEntry = data.FirstOrDefault(kv =>
            kv.Value is IList && kv.Key.IndexOf("Contour", StringComparison.OrdinalIgnoreCase) >= 0);

        if (contourEntry.Value is not IList contourPoints || contourPoints.Count < 3)
            return 0;

        var pts = new List<System.Drawing.Point>();
        foreach (var raw in contourPoints)
        {
            if (TryConvertPoint(raw, out var pt))
                pts.Add(pt);
        }

        if (pts.Count < 3)
            return 0;

        for (int i = 0; i < pts.Count; i++)
        {
            var from = pts[i];
            var to = pts[(i + 1) % pts.Count];
            CvInvoke.Line(target, from, to, new MCvScalar(0, 255, 0), 2);
        }

        return pts.Count;
    }

    /// <summary>
    /// 在影像上畫綠色輪廓線 + 黃色瑕疵資訊文字框。
    /// </summary>
    private static void DrawOverlay(
        Mat target,
        ProcessingResult mainResult,
        ProcessingResult? defectResult,
        string slotName)
    {
        var mainData = mainResult.AdditionalData as Dictionary<string, object>;
        var defectData = defectResult?.AdditionalData as Dictionary<string, object>;

        // 先畫主流程輪廓，畫不到再嘗試 defect 結果輪廓
        int drawn = DrawContourFromData(target, mainData);
        if (drawn == 0)
            drawn = DrawContourFromData(target, defectData);

        _logger.Debug($"[BumperAlg] {slotName}: contour points drawn = {drawn}");

        // ── 畫瑕疵量測文字框（黃色） ──
        var resultToShow = defectResult ?? mainResult;
        var lines = new List<string> { slotName };

        if (resultToShow.AdditionalData is Dictionary<string, object> data)
        {
            if (data.TryGetValue("DetectedArea", out var area))
                lines.Add($"Area: {area}");
            if (data.TryGetValue("DistanceToRoiCenter", out var dist))
                lines.Add($"Dist: {dist}");

            if (lines.Count == 1)
            {
                foreach (var kv in data.Take(4))
                    lines.Add($"{kv.Key}: {kv.Value}");
            }
        }

        int x = 10, y = 28, lineH = 24;
        int boxW = Math.Min(target.Width - 20, 420);
        int boxH = lines.Count * lineH + 14;

        CvInvoke.Rectangle(target,
            new System.Drawing.Rectangle(x - 6, y - 20, boxW, boxH),
            new MCvScalar(0, 0, 0), -1);
        CvInvoke.Rectangle(target,
            new System.Drawing.Rectangle(x - 6, y - 20, boxW, boxH),
            new MCvScalar(0, 255, 255), 2);

        for (int i = 0; i < lines.Count; i++)
        {
            CvInvoke.PutText(target, lines[i],
                new System.Drawing.Point(x, y + i * lineH),
                Emgu.CV.CvEnum.FontFace.HersheySimplex,
                0.6, new MCvScalar(0, 255, 255), 2);
        }
    }

    private static bool DetermineNg(ProcessingResult result)
    {
        if (!result.Success) return true;
        if (result.AdditionalData is Dictionary<string, object> data
            && data.TryGetValue("IsNg", out var val)
            && val is bool b)
            return b;
        return false;
    }

    /// <summary>
    /// 從 JSON 讀取 defectSettings 區塊（與 TestALG 相同邏輯）。
    /// </summary>
    private static DefectDetectionSettings LoadDefectSettingsFromJson(string jsonPath)
    {
        var settings = new DefectDetectionSettings { IsEnabled = true };
        try
        {
            string json = File.ReadAllText(jsonPath);
            var match = System.Text.RegularExpressions.Regex.Match(
                json, @"""defectSettings""\s*:\s*\{",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            if (!match.Success) return settings;

            int start = match.Index + match.Length - 1;
            int depth = 0;
            string? block = null;
            for (int i = start; i < json.Length; i++)
            {
                if (json[i] == '{') depth++;
                else if (json[i] == '}')
                {
                    depth--;
                    if (depth == 0) { block = json[start..(i + 1)]; break; }
                }
            }

            if (block == null) return settings;

            T Get<T>(string key, T def, Func<string, T> parse)
            {
                var m = System.Text.RegularExpressions.Regex.Match(block,
                    $"\"{key}\"\\s*:\\s*([^,\\}}]+)",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (m.Success) try { return parse(m.Groups[1].Value.Trim()); } catch { }
                return def;
            }

            settings.IsEnabled = Get("IsEnabled", true, s => bool.Parse(s));
            settings.DiffThreshold = Get("DiffThreshold", settings.DiffThreshold, s => int.Parse(s));
            settings.MinDefectArea = Get("MinDefectArea", settings.MinDefectArea, s => int.Parse(s));
            settings.MaxDefectArea = Get("MaxDefectArea", settings.MaxDefectArea, s => int.Parse(s));
            settings.MinAspectRatio = Get("MinAspectRatio", settings.MinAspectRatio,
                s => double.Parse(s, System.Globalization.CultureInfo.InvariantCulture));
            settings.MaxAspectRatio = Get("MaxAspectRatio", settings.MaxAspectRatio,
                s => double.Parse(s, System.Globalization.CultureInfo.InvariantCulture));
            settings.MergeKernelSize = Get("MergeKernelSize", settings.MergeKernelSize, s => int.Parse(s));
            settings.BlurKernelSize = Get("BlurKernelSize", settings.BlurKernelSize, s => int.Parse(s));
        }
        catch (Exception ex)
        {
            _logger.Warn($"讀取 defectSettings 失敗: {ex.Message}");
        }
        return settings;
    }

    /// <summary>
    /// Convert EmguCV Mat to WPF BitmapSource.
    /// Uses mat.Step (actual bytes per row including alignment padding) instead of
    /// Width * Channels — fixes all-white or colour-shifted output.
    /// </summary>
    private static BitmapSource MatToBitmapSource(Mat mat)
    {
        int stride = mat.Step;  // includes OpenCV memory-alignment padding
        byte[] pixels = new byte[mat.Height * stride];
        Marshal.Copy(mat.DataPointer, pixels, 0, pixels.Length);

        PixelFormat fmt = mat.NumberOfChannels == 1
            ? PixelFormats.Gray8
            : PixelFormats.Bgr24;

        return BitmapSource.Create(mat.Width, mat.Height, 96, 96, fmt, null, pixels, stride);
    }
}

/// <summary>
/// 演算法分析回傳結果。
/// </summary>
public readonly record struct BumperAlgResult(
    bool Success,
    bool IsNg,
    string Message,
    BitmapSource? Image)
{
    public static BumperAlgResult Fail(string message)
        => new(false, true, message, null);
}
