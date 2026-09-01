using BumperFlat.ImageProcessing;
using BumperFlat.ImageProcessing.Models;
using Emgu.CV;
using Emgu.CV.Structure;
using NLog;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Slot_Inspection.Services;

/// <summary>
/// 橋接演算法（新／舊兩套）與 Slot_Inspection。
/// 舊：BumperFlat.ImageProcessing.dll，需要 ROI/JSON 參數，每次呼叫即時 new。
/// 新：BumperFlatDetection.dll（AI 模型），不需要 ROI，但初始化很重（載入 GPU 模型），
///     整個服務生命週期只初始化一次，用 <see cref="InspectionConfig.UseNewBumperAlgorithm"/> 切換。
/// 流程：讀取已存檔影像 → 呼叫演算法分析 → 畫輪廓線/瑕疵疊加 → 回傳 BitmapSource。
/// </summary>
public sealed class BumperAlgService
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

    private readonly string _configDir;

    // ── AI演算法（BumperFlatDetection）延遲初始化 ──
    // 用 dynamic 而非強型別 BumperFlatDetector：這個 DLL 現在改由私有 AssemblyLoadContext
    // （BumperFlatDetectionLoadContext）載入，跟主程式屬於不同的型別載入身分，不能直接靜態參照它的型別。
    private dynamic? _newDetector;
    private static Assembly? _bumperFlatDetectionAssembly;
    private readonly SemaphoreSlim _newDetectorInitLock = new(1, 1);

    public BumperAlgService(string? configDir = null)
    {
        _configDir = configDir
            ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config");
    }

    /// <summary>
    /// 對已存檔的影像執行演算法分析，並回傳疊加結果的 BitmapSource。
    /// </summary>
    /// <param name="imagePath">已存檔的影像路徑（.tif）</param>
    /// <param name="jsonKey">Config 資料夾下的 JSON 檔名（不含 .json），例如 "Parameter"；
    /// 舊演算法路徑用來讀取完整的演算法參數，AI演算法路徑只會讀裡面的 Roi 欄位（使用者畫的框），
    /// 拿框的中心點當作 DistanceToRoiCenter 的參考點，讓新舊兩套演算法量的是同一個定義。</param>
    /// <param name="slotName">用於 Log 的 Slot 名稱</param>
    /// <param name="useNewAlgorithm">true=走新版 BumperFlatDetection AI 演算法；false=走原本的 BumperFlat.ImageProcessing</param>
    public async Task<BumperAlgResult> AnalyzeAsync(string imagePath, string jsonKey, string slotName, bool useNewAlgorithm)
    {
        if (useNewAlgorithm)
            return await AnalyzeNewAsync(imagePath, jsonKey, slotName);

        // 舊演算法本身是同步、CPU-bound 的呼叫，包一層 Task.Run 讓呼叫端維持統一的 async 介面
        return await Task.Run(() => AnalyzeLegacy(imagePath, jsonKey, slotName));
    }

    /// <summary>
    /// 舊演算法（BumperFlat.ImageProcessing）分析邏輯，行為與改動前的 Analyze() 完全相同。
    /// </summary>
    private BumperAlgResult AnalyzeLegacy(string imagePath, string jsonKey, string slotName)
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
            double detectedArea = 0.0;
            if (mainData != null && mainData.TryGetValue("DetectedArea", out var areaVal))
            {
                try
                {
                    // 原始值為像素面積，換算為 mm?（像素尺寸 0.0069 mm/px）
                    double pixelArea = Convert.ToDouble(areaVal);
                    detectedArea = pixelArea * 0.0069 * 0.0069;
                    // 鏡子角度補償係數（Config\Parameter.json 的 AreaCorrectionFactor，乘一次）
                    detectedArea *= GetAreaCorrectionFactor();
                    // 回寫換算後的值，讓 DLL WriteResultToCsv 寫出 mm? 而非像素數
                    mainData["DetectedArea"] = detectedArea;
                }
                catch { }
            }

            double meanIntensity = 0.0;
            if (mainData != null && mainData.TryGetValue("MeanIntensity", out var meanVal))
            {
                try { meanIntensity = Convert.ToDouble(meanVal); }
                catch { }
            }

            double distanceToRoiCenter = 0.0;
            if (mainData != null && mainData.TryGetValue("DistanceToRoiCenter", out var distVal))
            {
                try { distanceToRoiCenter = Convert.ToDouble(distVal); }
                catch { }
            }

            var defectBoxes = new List<System.Drawing.Rectangle>();
            if (mainData != null && mainData.TryGetValue("DefectBoundingBoxes", out var boxesVal) && boxesVal is IList boxList)
            {
                foreach (var item in boxList)
                    if (TryConvertRect(item, out var rect))
                        defectBoxes.Add(rect);
            }

            var bitmapSource = MatToBitmapSource(overlay);
            bitmapSource.Freeze();
            return new BumperAlgResult(true, isNg, isNg ? "NG" : "OK", bitmapSource, detectedArea, result,
                meanIntensity, distanceToRoiCenter, defectBoxes);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BumperAlg] {slotName}: ? 例外: {ex.Message}");
            return BumperAlgResult.Fail(ex.Message);
        }
    }

    // ─────────────────────────────────────────────
    //  AI演算法（BumperFlatDetection AI 模型）
    // ─────────────────────────────────────────────

    /// <summary>
    /// 確保 <see cref="_newDetector"/> 已初始化（第一次用到AI演算法時才載入 GPU 模型，避免拖慢
    /// UseNewBumperAlgorithm=false 現場的開機時間）。用 SemaphoreSlim 避免多執行緒同時觸發重複初始化。
    /// </summary>
    private async Task<dynamic> EnsureNewDetectorReadyAsync()
    {
        if (_newDetector != null)
            return _newDetector;

        await _newDetectorInitLock.WaitAsync();
        try
        {
            if (_newDetector != null)
                return _newDetector;

            Assembly assembly = GetBumperFlatDetectionAssembly();
            Type detectorType = assembly.GetType("BumperFlatDetection.BumperFlatDetector")
                ?? throw new InvalidOperationException("BumperFlatDetection.dll 裡找不到型別 BumperFlatDetection.BumperFlatDetector");
            Type optionsType = assembly.GetType("BumperFlatDetection.BumperFlatDetectorOptions")
                ?? throw new InvalidOperationException("BumperFlatDetection.dll 裡找不到型別 BumperFlatDetection.BumperFlatDetectorOptions");

            dynamic options = LoadNewAlgorithmOptions(optionsType);

            bool modelFileExists = File.Exists((string)options.ModelPath);
            _logger.Info($"[BumperAlg] 準備載入AI演算法模型：ModelPath={options.ModelPath}, " +
                $"存在={modelFileExists}" +
                (modelFileExists
                    ? $", 大小={new FileInfo((string)options.ModelPath).Length} bytes, 修改時間={File.GetLastWriteTime((string)options.ModelPath):yyyy-MM-dd HH:mm:ss}"
                    : string.Empty));

            dynamic detector = Activator.CreateInstance(detectorType)!;
            bool loaded;

            // 廠商獨立測試工具（雙擊執行的主控台程式）跟同一份模型檔測試會成功，Slot_Inspection
            // 用一模一樣的檔案卻失敗——目前唯一還沒排除的差異：獨立工具的「目前工作目錄」就是它
            // 自己所在資料夾，WPF 主程式啟動時的工作目錄不一定是 exe 所在資料夾。有些原生 SDK
            // 會用工作目錄的相對路徑去找授權/暫存檔，所以呼叫 InitializeAsync 前暫時切過去試看看。
            string originalCwd = Environment.CurrentDirectory;
            string privateDir = Path.GetDirectoryName(assembly.Location) ?? originalCwd;
            try
            {
                Environment.CurrentDirectory = privateDir;
                _logger.Debug($"[BumperAlg] 呼叫 InitializeAsync 前暫時切換工作目錄：{originalCwd} → {privateDir}");

                loaded = await detector.InitializeAsync(options);
            }
            catch (Exception ex)
            {
                // InitializeAsync 理論上失敗時回傳 false（見 README），這裡是防呆：
                // 萬一是組件載入失敗之類在呼叫當下直接拋出的例外，也要完整記錄，不能只留下 false 這個籠統結果。
                detector.Dispose();
                _logger.Error(ex, $"[BumperAlg] detector.InitializeAsync 呼叫本身拋出例外（ModelPath={options.ModelPath}）");
                throw;
            }
            finally
            {
                Environment.CurrentDirectory = originalCwd;
            }

            if (!loaded)
            {
                detector.Dispose();

                // InitializeAsync 只回傳 bool，不會告訴我們真正原因；
                // 把目前 process 裡（含私有 ALC）實際載入的 Emgu.CV 組件版本記下來，方便後續排查。
                string loadedEmguVersions = string.Join("; ", AppDomain.CurrentDomain.GetAssemblies()
                    .Where(a => a.GetName().Name == "Emgu.CV")
                    .Select(a => $"{a.GetName().Version} @ {a.Location} (ALC={AssemblyLoadContext.GetLoadContext(a)?.Name ?? "Default"})"));
                _logger.Error($"[BumperAlg] InitializeAsync 回傳 false（未拋例外）。" +
                    $"目前 process 內載入的 Emgu.CV 組件：{(string.IsNullOrEmpty(loadedEmguVersions) ? "(尚未載入任何 Emgu.CV 組件)" : loadedEmguVersions)}");

                throw new InvalidOperationException(
                    $"BumperFlatDetection 模型載入失敗（ModelPath={options.ModelPath}）：請確認模型檔存在、未過期。詳細診斷資訊已寫入 log。");
            }

            _logger.Info($"[BumperAlg] AI演算法(BumperFlatDetection)已載入，GPU 加速：{detector.IsGpuEnabled}");
            _newDetector = detector;
            return _newDetector;
        }
        finally
        {
            _newDetectorInitLock.Release();
        }
    }

    /// <summary>
    /// 用私有 AssemblyLoadContext（<see cref="BumperFlatDetectionLoadContext"/>）載入
    /// BumperFlatDetectionPrivate\BumperFlatDetection.dll，讓它跟自己那份 Emgu.CV（4.8.1.5350）
    /// 跟主程式的 Emgu.CV（4.12.0.5764）互不干擾。只在第一次用到AI演算法時載入一次。
    /// </summary>
    private static Assembly GetBumperFlatDetectionAssembly()
    {
        if (_bumperFlatDetectionAssembly != null)
            return _bumperFlatDetectionAssembly;

        string privateDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BumperFlatDetectionPrivate");
        string dllPath = Path.Combine(privateDir, "BumperFlatDetection.dll");
        var context = new BumperFlatDetectionLoadContext(privateDir);
        _bumperFlatDetectionAssembly = context.LoadFromAssemblyPath(dllPath);
        return _bumperFlatDetectionAssembly;
    }

    /// <summary>從 Config\BumperFlatDetectionOptions.json 讀取AI演算法選項；檔案不存在時使用內建預設值。</summary>
    private dynamic LoadNewAlgorithmOptions(Type optionsType)
    {
        dynamic options = Activator.CreateInstance(optionsType)!;
        options.ModelPath = Path.Combine(_configDir, "Models", "bumperflat.dll");
        options.ConfidenceThreshold = 0.7f;
        options.IoUThreshold = 0.7f;
        options.PolishingAreaSpecMin = 50.0;
        options.PolishingAreaSpecMax = 200.0;
        options.PixelToMm2Factor = 0.00004761;

        try
        {
            string path = Path.Combine(_configDir, "BumperFlatDetectionOptions.json");
            if (!File.Exists(path)) return options;

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            if (root.TryGetProperty("ConfidenceThreshold", out var c)) options.ConfidenceThreshold = c.GetSingle();
            if (root.TryGetProperty("IoUThreshold", out var i)) options.IoUThreshold = i.GetSingle();
            if (root.TryGetProperty("PolishingAreaSpecMin", out var mn)) options.PolishingAreaSpecMin = mn.GetDouble();
            if (root.TryGetProperty("PolishingAreaSpecMax", out var mx)) options.PolishingAreaSpecMax = mx.GetDouble();
            if (root.TryGetProperty("PixelToMm2Factor", out var f)) options.PixelToMm2Factor = f.GetDouble();
        }
        catch (Exception ex)
        {
            _logger.Warn($"讀取 BumperFlatDetectionOptions.json 失敗，改用內建預設值: {ex.Message}");
        }

        return options;
    }

    /// <summary>
    /// AI演算法（BumperFlatDetection）分析邏輯。偵測本身不需要 ROI/JSON（模型自己在整張影像裡找目標），
    /// 但會另外讀 jsonKey 對應 JSON 的 Roi 欄位（使用者畫的框），拿框的中心點當 DistanceToRoiCenter 參考點。
    /// </summary>
    private async Task<BumperAlgResult> AnalyzeNewAsync(string imagePath, string jsonKey, string slotName)
    {
        try
        {
            if (!File.Exists(imagePath))
                return BumperAlgResult.Fail($"影像不存在: {imagePath}");

            dynamic detector = await EnsureNewDetectorReadyAsync();
            dynamic rawResults = await detector.DetectAsync(imagePath);
            List<NewAlgDetection> results = ConvertDynamicResults(rawResults);

            using Mat original = CvInvoke.Imread(imagePath, Emgu.CV.CvEnum.ImreadModes.AnyColor);
            if (original == null || original.IsEmpty)
                return BumperAlgResult.Fail("無法讀取原圖");

            // 廠商 DrawResults() 固定畫紅色框，改用同一組 Polygon/BoundingBox 自己畫綠色，
            // 跟舊演算法（DrawContourFromData）的疊圖樣式保持一致。
            using Mat annotated = DrawResultsGreen(original, results);
            var bitmapSource = MatToBitmapSource(annotated);
            bitmapSource.Freeze();

            var roiCenter = LoadRoiCenter(jsonKey);
            var mapped = MapNewAlgorithmResult(results, original.Width, original.Height, roiCenter);

            // 跟舊演算法（AnalyzeLegacy）套用同一個鏡子角度補償係數，避免兩套演算法算出來的面積
            // 只因為少乘這個係數就系統性差了一截，報表 Value 欄位才能維持同一套換算基準。
            double detectedArea = mapped.DetectedArea * GetAreaCorrectionFactor();

            _logger.Info($"[BumperAlg] {slotName}（AI演算法）: 偵測 {results.Count} 筆結果，" +
                $"IsNg={mapped.IsNg}, DetectedArea={detectedArea:F2}mm²（已乘 AreaCorrectionFactor），" +
                $"RoiCenter={(roiCenter.HasValue ? $"({roiCenter.Value.X:F0},{roiCenter.Value.Y:F0})來自{jsonKey}.json" : "找不到，改用影像正中心頂替")}");

            return new BumperAlgResult(true, mapped.IsNg, mapped.IsNg ? "NG" : "OK", bitmapSource,
                detectedArea, null, mapped.MeanIntensity, mapped.DistanceToRoiCenter, mapped.DefectBoxes);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, $"[BumperAlg] {slotName}（AI演算法）例外");
            return BumperAlgResult.Fail(ex.Message);
        }
    }

    /// <summary>
    /// 把私有 ALC 載入的 BumperFlatDetectionResult（dynamic，型別身分跟主程式不同）轉成
    /// 本專案自己的 <see cref="NewAlgDetection"/>，讓 MapNewAlgorithmResult/DrawResultsGreen
    /// 之後可以維持強型別，不用整段都改成 dynamic。
    /// </summary>
    private static List<NewAlgDetection> ConvertDynamicResults(dynamic rawResults)
    {
        var list = new List<NewAlgDetection>();
        foreach (dynamic r in (IEnumerable)rawResults)
        {
            list.Add(new NewAlgDetection(
                ClassName: (string)r.ClassName,
                Confidence: (float)r.Confidence,
                BoundingBox: (System.Drawing.Rectangle)r.BoundingBox,
                Polygon: (List<System.Drawing.Point>)r.Polygon,
                Area: (double)r.Area,
                GrayValue: (double)r.GrayValue,
                StdValue: (double)r.StdValue,
                AI: (string)r.AI,
                Notes: (string)(r.Notes ?? string.Empty)));
        }
        return list;
    }

    /// <summary>
    /// 讀取 Config\{jsonKey}.json 的 Roi 欄位（使用者畫的框：X/Y/Width/Height），算出框的中心點，
    /// 當作AI演算法 DistanceToRoiCenter 的參考點，讓定義跟舊演算法（量測物件偏離使用者畫的框多遠）一致。
    /// 檔案不存在、沒有 Roi 欄位、或讀取失敗時回傳 null，呼叫端會 fallback 用影像正中心頂替。
    /// </summary>
    private (double X, double Y)? LoadRoiCenter(string jsonKey)
    {
        try
        {
            string jsonPath = Path.Combine(_configDir, jsonKey + ".json");
            if (!File.Exists(jsonPath)) return null;

            using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
            if (!doc.RootElement.TryGetProperty("Roi", out var roi)) return null;

            double x = roi.GetProperty("X").GetDouble();
            double y = roi.GetProperty("Y").GetDouble();
            double w = roi.GetProperty("Width").GetDouble();
            double h = roi.GetProperty("Height").GetDouble();
            return (x + w / 2.0, y + h / 2.0);
        }
        catch (Exception ex)
        {
            _logger.Warn($"讀取 {jsonKey}.json 的 Roi 欄位失敗，DistanceToRoiCenter 將改用影像正中心頂替: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 把AI演算法回傳的多筆偵測結果，合併成單一數值結果（DetectedArea/IsNg/...）。
    ///
    /// TODO: 待確認——以下是暫定的合併規則，之後跟現場確認後再調整（只需要改這個方法，其他都不用動）：
    ///   - IsNg：沒有 Polishing 結果、或任一結果 AI=="NG"，就視為 NG。
    ///   - DetectedArea / MeanIntensity：取 ClassName=="Polishing" 的第一筆（理論上一張圖只會有一個磨面）。
    ///   - DefectBoundingBoxes：所有 ClassName=="BumperFlatDefect" 的偵測框。
    ///
    /// DistanceToRoiCenter：Polishing 偵測框中心，到 <paramref name="roiCenter"/>（使用者畫的 Roi 框中心，
    /// 來自對應 Slot 的 JSON 檔）的距離（像素）；roiCenter 讀不到時 fallback 用影像正中心頂替。
    /// </summary>
    private static (bool IsNg, double DetectedArea, double MeanIntensity, double DistanceToRoiCenter,
        List<System.Drawing.Rectangle> DefectBoxes) MapNewAlgorithmResult(
        List<NewAlgDetection> results, int imageWidth, int imageHeight, (double X, double Y)? roiCenter)
    {
        var polishing = results.FirstOrDefault(r => r.ClassName == "Polishing");
        bool hasPolishing = polishing != null;

        bool isNg = !hasPolishing || results.Any(r => r.AI == "NG");
        double detectedArea = hasPolishing ? polishing!.Area : 0.0;
        double meanIntensity = hasPolishing ? polishing!.GrayValue : 0.0;

        double distanceToRoiCenter = 0.0;
        if (hasPolishing)
        {
            var box = polishing!.BoundingBox;
            double cx = box.X + box.Width / 2.0;
            double cy = box.Y + box.Height / 2.0;
            double refX = roiCenter?.X ?? imageWidth / 2.0;
            double refY = roiCenter?.Y ?? imageHeight / 2.0;
            double dx = cx - refX;
            double dy = cy - refY;
            distanceToRoiCenter = Math.Sqrt(dx * dx + dy * dy);
        }

        var defectBoxes = results
            .Where(r => r.ClassName == "BumperFlatDefect")
            .Select(r => r.BoundingBox)
            .ToList();

        return (isNg, detectedArea, meanIntensity, distanceToRoiCenter, defectBoxes);
    }

    /// <summary>釋放AI演算法（BumperFlatDetection）已載入的模型資源，供 MachineController.Dispose() 呼叫。</summary>
    public void DisposeNewDetector()
    {
        _newDetector?.Dispose();
        _newDetector = null;
        // 不 Unload 私有 AssemblyLoadContext（isCollectible=false 本來就不支援）也不清掉
        // _bumperFlatDetectionAssembly 快取：下次 EnsureNewDetectorReadyAsync() 只要重新
        // Activator.CreateInstance 一個新的 detector 執行個體即可，不需要重新載入整個 DLL。
    }

    /// <summary>
    /// 讀取 Config\Parameter.json 的 AreaCorrectionFactor（鏡子角度補償係數）。
    /// 每次都重新讀檔，讓現場調整係數後不需重啟程式即可生效；讀取失敗或欄位不存在則回傳 1.33（沿用原本寫死的值）。
    /// </summary>
    private double GetAreaCorrectionFactor()
    {
        try
        {
            string path = Path.Combine(_configDir, "Parameter.json");
            if (!File.Exists(path)) return 1.33;

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.TryGetProperty("AreaCorrectionFactor", out var prop))
                return prop.GetDouble();
        }
        catch (Exception ex)
        {
            _logger.Warn($"讀取 AreaCorrectionFactor 失敗，改用預設值 1.33: {ex.Message}");
        }

        return 1.33;
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

    private static bool TryConvertRect(object? source, out System.Drawing.Rectangle rect)
    {
        if (source is System.Drawing.Rectangle r)
        {
            rect = r;
            return true;
        }

        if (source is int[] ia && ia.Length >= 4)
        {
            rect = new System.Drawing.Rectangle(ia[0], ia[1], ia[2], ia[3]);
            return true;
        }

        if (source is double[] da2 && da2.Length >= 4)
        {
            rect = new System.Drawing.Rectangle(
                (int)Math.Round(da2[0]), (int)Math.Round(da2[1]),
                (int)Math.Round(da2[2]), (int)Math.Round(da2[3]));
            return true;
        }

        rect = default;
        return false;
    }

    /// <summary>
    /// AI演算法（BumperFlatDetection）疊圖：每筆結果用 Polygon 畫綠色輪廓，
    /// Polygon 點數 &lt; 3（退化成純矩形框）時改畫 BoundingBox。
    /// </summary>
    private static Mat DrawResultsGreen(Mat original, IEnumerable<NewAlgDetection> results)
    {
        Mat overlay = original.Clone();
        var green = new MCvScalar(0, 255, 0);

        foreach (var r in results)
        {
            if (r.Polygon != null && r.Polygon.Count >= 3)
            {
                for (int i = 0; i < r.Polygon.Count; i++)
                {
                    var from = r.Polygon[i];
                    var to = r.Polygon[(i + 1) % r.Polygon.Count];
                    CvInvoke.Line(overlay, from, to, green, 2);
                }
            }
            else
            {
                CvInvoke.Rectangle(overlay, r.BoundingBox, green, 2);
            }
        }

        return overlay;
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

    /// <summary>
    /// AI演算法（BumperFlatDetection，透過私有 AssemblyLoadContext 載入）單筆偵測結果的本地鏡射型別。
    /// 欄位對應廠商 BumperFlatDetectionResult，讓 MapNewAlgorithmResult/DrawResultsGreen 可以維持
    /// 強型別，不用整段改成 dynamic；一定要是參照型別（record，不是 record struct），
    /// 才能讓 MapNewAlgorithmResult 裡 FirstOrDefault 找不到時回傳 null 的邏輯照舊成立。
    /// </summary>
    private sealed record NewAlgDetection(
        string ClassName,
        float Confidence,
        System.Drawing.Rectangle BoundingBox,
        List<System.Drawing.Point> Polygon,
        double Area,
        double GrayValue,
        double StdValue,
        string AI,
        string Notes);
}

/// <summary>
/// 演算法分析回傳結果。
/// </summary>
public readonly record struct BumperAlgResult(
    bool Success,
    bool IsNg,
    string Message,
    BitmapSource? Image,
    double DetectedArea = 0.0,
    ProcessingResult? RawResult = null,
    double MeanIntensity = 0.0,
    double DistanceToRoiCenter = 0.0,
    IReadOnlyList<System.Drawing.Rectangle>? DefectBoundingBoxes = null)
{
    public IReadOnlyList<System.Drawing.Rectangle> DefectBoundingBoxes { get; init; } =
        DefectBoundingBoxes ?? Array.Empty<System.Drawing.Rectangle>();

    public static BumperAlgResult Fail(string message)
        => new(false, true, message, null, 0.0, null);
}
