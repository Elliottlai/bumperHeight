using BumperFlat.ImageProcessing;
using BumperFlat.ImageProcessing.Logging;
using BumperFlat.ImageProcessing.Models;
using Emgu.CV;
using Emgu.CV.Structure;
using Microsoft.Win32;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TestALG
{
    /// <summary>
    /// MainWindow.xaml 的互動邏輯。
    /// 
    /// 【ImageProcess DLL 使用重點】
    /// 1. 建立 DLL 物件：new ImageProcessor()
    /// 2. 主流程呼叫：ProcessImage(imagePath, parametersJsonPath, saveDebugImage)
    ///    - imagePath：輸入影像完整路徑
    ///    - parametersJsonPath：參數 JSON 路徑（本專案為 config\{imageName}.json）
    ///    - saveDebugImage：是否輸出除錯中間圖（本專案固定 false）
    /// 3. 檢查 ProcessingResult.Success，若 false 以 ErrorMessage 顯示錯誤
    /// 4. 讀取輸出影像
    ///    - 優先使用 ProcessedImageData（byte[]，記憶體資料）
    ///    - 其次使用 ProcessedImagePath（磁碟檔案）
    /// 5. 若 AdditionalData 含有 Contour，可再呼叫
    ///    DetectDefectsInContour(imagePath, contourPoints, defectSettings, saveDebugImage)
    ///    執行瑕疵偵測。
    /// 
    /// 其他 UI 專案可直接複製 RunEmguCV / LoadDefectSettingsFromJson / DrawDefectOverlay 這三段整合模式。
    /// </summary>
    public partial class MainWindow : Window
    {
        /// <summary>
        /// 目前選取的輸入影像完整路徑。
        /// </summary>
        private string _imagePath = string.Empty;

        /// <summary>
        /// 傳給 DLL 的參數 JSON 完整路徑。
        /// </summary>
        private string _parametersJsonPath = string.Empty;

        /// <summary>
        /// 最近一次 DLL 執行結果，供「顯示瑕疵結果」按鈕重用。
        /// </summary>
        private ProcessingResult _lastProcessingResult;

        /// <summary>
        /// 最近一次處理後影像，避免重複呼叫 DLL。
        /// </summary>
        private Mat _lastProcessedMat;

        public MainWindow()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 選取輸入影像，並自動推導對應的 JSON 設定檔路徑。
        /// </summary>
        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dlg = new OpenFileDialog
            {
                Title = "選取影像檔案",
                Filter = "影像檔案|*.bmp;*.jpg;*.jpeg;*.png;*.tif;*.tiff|所有檔案|*.*"
            };

            if (dlg.ShowDialog() == true)
            {
                _imagePath = dlg.FileName;
                ImagePathTextBox.Text = _imagePath;

                // 約定：JSON 放在執行檔目錄下的 config 資料夾，
                // 檔名取「副檔名前最後一個底線 '_' 後的字串」。
                string imageNameWithoutExt = System.IO.Path.GetFileNameWithoutExtension(_imagePath);
                string jsonKey = imageNameWithoutExt;
                int lastUnderscoreIndex = imageNameWithoutExt.LastIndexOf('_');
                if (lastUnderscoreIndex >= 0 && lastUnderscoreIndex < imageNameWithoutExt.Length - 1)
                {
                    jsonKey = imageNameWithoutExt.Substring(lastUnderscoreIndex + 1).Trim();
                }

                string exeDir = AppDomain.CurrentDomain.BaseDirectory;
                _parametersJsonPath = System.IO.Path.Combine(exeDir, "config", jsonKey + ".json");
                ParametersJsonPathTextBox.Text = _parametersJsonPath;

                _lastProcessingResult = null;
                _lastProcessedMat?.Dispose();
                _lastProcessedMat = null;
                ResultImage.Source = null;
            }
        }

        /// <summary>
        /// 執行演算法 DLL，並將結果影像顯示在 UI。
        /// </summary>
        private void RunButton_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateInput())
            {
                return;
            }

            var result = RunEmguCV(_imagePath, _parametersJsonPath);
            if (result != null && !result.IsEmpty)
            {
                _lastProcessedMat?.Dispose();
                _lastProcessedMat = result;
                ResultImage.Source = MatToBitmapSource(_lastProcessedMat);
            }
        }

        /// <summary>
        /// 將瑕疵偵測結果疊加到影像（文字框）後顯示於 UI。
        /// </summary>
        private void ShowDefectButton_Click(object sender, RoutedEventArgs e)
        {
            if (_lastProcessedMat == null || _lastProcessedMat.IsEmpty || _lastProcessingResult == null)
            {
                if (!ValidateInput())
                {
                    return;
                }

                var processor = new ImageProcessor();
                var result = RunEmguCV(_imagePath, _parametersJsonPath);
                

                if (result == null || result.IsEmpty)
                {
                    return;
                }
                // 第三個參數依 DLL 定義：此專案固定傳 false。
                

                _lastProcessedMat?.Dispose();
                _lastProcessedMat = result;
            }

            using (Mat overlay = _lastProcessedMat.Clone())
            {
                DrawDefectOverlay(overlay, _lastProcessingResult);
                ResultImage.Source = MatToBitmapSource(overlay);
            }
        }

        /// <summary>
        /// DLL 呼叫入口：
        /// 1) 先做主流程 ProcessImage
        /// 2) 若有輪廓資料再做 DetectDefectsInContour
        /// 3) 回傳可顯示於 WPF 的 Mat
        /// </summary>
        /// <param name="imagePath">輸入影像完整路徑（給 DLL 讀圖）。</param>
        /// <param name="parametersJsonPath">參數 JSON 路徑（給 DLL 讀取演算法參數）。</param>
        /// <returns>處理後影像 Mat；失敗或無影像時回傳 null。</returns>
        private Mat RunEmguCV(string imagePath, string parametersJsonPath)
        {
            // 每次執行建立一個 ImageProcessor 實例。
            // 若未來 DLL 需要共用資源，可改成欄位或 DI 管理生命週期。
            var processor = new ImageProcessor();

            // DLL 主函式 #1：ProcessImage
            // 回傳 ProcessingResult，內含：
            // - Success / ErrorMessage：成功狀態
            // - ProcessedImageData：記憶體影像位元組
            // - ProcessedImagePath：輸出檔路徑
            // - AdditionalData：額外結果（例如 Contour、量測值）
            ProcessingResult results = processor.ProcessImage(imagePath, parametersJsonPath, false);
            _lastProcessingResult = results;

            if (!results.Success)
            {
                MessageBox.Show($"處理失敗：{results.ErrorMessage}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                return null;
            }

            // 輸出取得策略：
            // 先取 ProcessedImageData（速度較快，不需磁碟 I/O）
            // 再退回 ProcessedImagePath（當 DLL 僅輸出檔案時）
            if (results.ProcessedImageData != null && results.ProcessedImageData.Length > 0)
            {
                Mat decoded = new Mat();
                CvInvoke.Imdecode(results.ProcessedImageData, Emgu.CV.CvEnum.ImreadModes.AnyColor, decoded);
                return decoded;
            }

            // 若主流程有提供輪廓 Contour，接著進行瑕疵偵測子流程。
            var additionalData = results.AdditionalData as Dictionary<string, object>;
            if (additionalData != null && additionalData.ContainsKey("Contour"))
            {
                // DLL 回傳的輪廓資料使用 IList 接收，直接原樣傳回 DLL 進行下一階段分析。
                var contourPoints = additionalData["Contour"] as IList;
                if (contourPoints == null || contourPoints.Count < 3)
                {
                    MessageBox.Show("輪廓點不足，無法執行瑕疵偵測。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                else
                {
                    // defectSettings 由 JSON 的 defectSettings 區段讀取，讓參數可由現場調整。
                    var defectSettings = LoadDefectSettingsFromJson(parametersJsonPath);

                    // DLL 主函式 #2：DetectDefectsInContour
                    // 依輪廓與 defectSettings 執行瑕疵分析。
                    ProcessingResult defectResults = processor.DetectDefectsInContour(imagePath, contourPoints, defectSettings, false);

                    // 若瑕疵偵測成功，覆蓋最後結果，供 UI 疊圖顯示。
                    if (defectResults != null && defectResults.Success)
                    {
                        _lastProcessingResult = defectResults;
                    }
                }
            }

            if (!string.IsNullOrEmpty(results.ProcessedImagePath) && File.Exists(results.ProcessedImagePath))
            {
                return CvInvoke.Imread(results.ProcessedImagePath);
            }

            MessageBox.Show("處理完成，但無輸出影像。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return null;
        }

        /// <summary>
        /// 從參數 JSON 讀取 defectSettings 區塊，轉成 DefectDetectionSettings。
        /// 
        /// JSON 範例：
        /// "defectSettings": {
        ///   "IsEnabled": true,
        ///   "DiffThreshold": 25,
        ///   "MinDefectArea": 10,
        ///   "MaxDefectArea": 5000,
        ///   "MinAspectRatio": 0.2,
        ///   "MaxAspectRatio": 5.0,
        ///   "MergeKernelSize": 3,
        ///   "BlurKernelSize": 5
        /// }
        /// </summary>
        /// <param name="jsonPath">參數 JSON 檔案完整路徑。</param>
        /// <returns>可直接傳給 DetectDefectsInContour 的設定物件。</returns>
        private DefectDetectionSettings LoadDefectSettingsFromJson(string jsonPath)
        {
            var settings = new DefectDetectionSettings
            {
                IsEnabled = true
            };

            if (string.IsNullOrWhiteSpace(jsonPath) || !File.Exists(jsonPath))
            {
                return settings;
            }

            try
            {
                var json = File.ReadAllText(jsonPath);
                var defectBlock = ExtractDefectSettingsBlock(json);
                if (string.IsNullOrEmpty(defectBlock))
                {
                    return settings;
                }

                settings.IsEnabled = TryGetBool(defectBlock, "IsEnabled", settings.IsEnabled);
                settings.DiffThreshold = TryGetInt(defectBlock, "DiffThreshold", settings.DiffThreshold);
                settings.MinDefectArea = TryGetInt(defectBlock, "MinDefectArea", settings.MinDefectArea);
                settings.MaxDefectArea = TryGetInt(defectBlock, "MaxDefectArea", settings.MaxDefectArea);
                settings.MinAspectRatio = TryGetDouble(defectBlock, "MinAspectRatio", settings.MinAspectRatio);
                settings.MaxAspectRatio = TryGetDouble(defectBlock, "MaxAspectRatio", settings.MaxAspectRatio);
                settings.MergeKernelSize = TryGetInt(defectBlock, "MergeKernelSize", settings.MergeKernelSize);
                settings.BlurKernelSize = TryGetInt(defectBlock, "BlurKernelSize", settings.BlurKernelSize);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"讀取 defectSettings 失敗：{ex.Message}", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            return settings;
        }

        /// <summary>
        /// 從完整 JSON 字串中擷取 defectSettings 物件文字區段。
        /// 這裡使用大括號深度計數，避免單純正則遇到巢狀物件時截斷。
        /// </summary>
        private string ExtractDefectSettingsBlock(string json)
        {
            var keyRegex = new Regex("\"defectSettings\"\\s*:\\s*\\{", RegexOptions.IgnoreCase);
            var match = keyRegex.Match(json);
            if (!match.Success)
            {
                return null;
            }

            int start = match.Index + match.Length - 1;
            int depth = 0;
            for (int i = start; i < json.Length; i++)
            {
                if (json[i] == '{') depth++;
                else if (json[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return json.Substring(start, i - start + 1);
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// 從 JSON 區塊讀取整數欄位；讀不到時保留預設值。
        /// </summary>
        private int TryGetInt(string jsonBlock, string key, int defaultValue)
        {
            var m = Regex.Match(jsonBlock, "\\\"" + Regex.Escape(key) + "\\\"\\s*:\\s*(-?\\d+)", RegexOptions.IgnoreCase);
            if (m.Success && int.TryParse(m.Groups[1].Value, out int value))
            {
                return value;
            }

            return defaultValue;
        }

        /// <summary>
        /// 從 JSON 區塊讀取浮點欄位（InvariantCulture）；讀不到時保留預設值。
        /// </summary>
        private double TryGetDouble(string jsonBlock, string key, double defaultValue)
        {
            var m = Regex.Match(jsonBlock, "\\\"" + Regex.Escape(key) + "\\\"\\s*:\\s*(-?\\d+(?:\\.\\d+)?)", RegexOptions.IgnoreCase);
            if (m.Success && double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double value))
            {
                return value;
            }

            return defaultValue;
        }

        /// <summary>
        /// 從 JSON 區塊讀取布林欄位；讀不到時保留預設值。
        /// </summary>
        private bool TryGetBool(string jsonBlock, string key, bool defaultValue)
        {
            var m = Regex.Match(jsonBlock, "\\\"" + Regex.Escape(key) + "\\\"\\s*:\\s*(true|false)", RegexOptions.IgnoreCase);
            if (m.Success && bool.TryParse(m.Groups[1].Value, out bool value))
            {
                return value;
            }

            return defaultValue;
        }

        /// <summary>
        /// 將 DLL 回傳結果疊加到影像上。
        /// 目前顯示 AdditionalData 中常用欄位（DetectedArea / DistanceToRoiCenter）。
        /// 若欄位名稱不同，會退回顯示前幾個 key-value 供除錯。
        /// </summary>
        private void DrawDefectOverlay(Mat target, ProcessingResult result)
        {
            var lines = new List<string>();
            lines.Add("Defect Detection Result");

            var additionalData = result.AdditionalData as Dictionary<string, object>;
            if (additionalData != null)
            {
                if (additionalData.ContainsKey("DetectedArea"))
                {
                    lines.Add($"DetectedArea: {additionalData["DetectedArea"]}");
                }

                if (additionalData.ContainsKey("DistanceToRoiCenter"))
                {
                    lines.Add($"DistanceToRoiCenter: {additionalData["DistanceToRoiCenter"]}");
                }

                if (lines.Count == 1)
                {
                    foreach (var kv in additionalData)
                    {
                        lines.Add($"{kv.Key}: {kv.Value}");
                        if (lines.Count >= 6)
                        {
                            break;
                        }
                    }
                }
            }
            else
            {
                lines.Add("No AdditionalData");
            }

            int startX = 12;
            int startY = 28;
            int lineHeight = 26;
            int boxWidth = Math.Min(target.Width - 20, 520);
            int boxHeight = Math.Max(60, (lines.Count * lineHeight) + 16);

            CvInvoke.Rectangle(target, new Rectangle(startX - 8, startY - 20, boxWidth, boxHeight), new MCvScalar(0, 0, 0), -1);
            CvInvoke.Rectangle(target, new Rectangle(startX - 8, startY - 20, boxWidth, boxHeight), new MCvScalar(0, 255, 255), 2);

            for (int i = 0; i < lines.Count; i++)
            {
                CvInvoke.PutText(
                    target,
                    lines[i],
                    new System.Drawing.Point(startX, startY + i * lineHeight),
                    Emgu.CV.CvEnum.FontFace.HersheySimplex,
                    0.65,
                    new MCvScalar(0, 255, 255),
                    2);
            }
        }

        /// <summary>
        /// 檢查必要輸入是否完整。
        /// </summary>
        private bool ValidateInput()
        {
            if (string.IsNullOrEmpty(_imagePath))
            {
                MessageBox.Show("請先選取影像檔案。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            return true;
        }

        /// <summary>
        /// 將 EmguCV Mat 轉為 WPF BitmapSource。
        /// 這是 DLL(OpenCV) 與 WPF UI 顯示之間的關鍵轉接函式。
        /// </summary>
        private BitmapSource MatToBitmapSource(Mat mat)
        {
            int stride = mat.Width * mat.NumberOfChannels;
            byte[] pixels = new byte[mat.Height * stride];
            Marshal.Copy(mat.DataPointer, pixels, 0, pixels.Length);

            PixelFormat format = mat.NumberOfChannels == 1
                ? PixelFormats.Gray8
                : PixelFormats.Bgr24;

            return BitmapSource.Create(
                mat.Width, mat.Height,
                96, 96,
                format,
                null,
                pixels,
                stride);
        }
    }
}
